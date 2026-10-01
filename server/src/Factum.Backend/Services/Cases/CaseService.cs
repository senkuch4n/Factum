using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Factum.Backend.Services.Reports;

namespace Factum.Backend.Services.Cases;

public interface ICaseService
{
    Task<List<Case>> ListAsync(string officerDni, CancellationToken ct = default);
    Task<Result<(Case Case, List<FileInfoDto> Files)>> GetAsync(string id, string officerDni,
        CancellationToken ct = default);
    Task<Case> CreateAsync(CreateCaseRequest request, User officer, CancellationToken ct = default);
    Task<Result<FileInfoDto>> UploadFileAsync(string id, string officerDni, string filename,
        string? sourcePath, Stream content, CancellationToken ct = default);
    Task<Result<List<FileInfoDto>>> ListFilesAsync(string id, string officerDni,
        CancellationToken ct = default);
    Task<Result<GenerateResponse>> GenerateAsync(string id, string officerDni,
        CancellationToken ct = default);
    Task<Result<(string Path, string ContentType, string FileName)>> DownloadAsync(
        string id, string filename, string officerDni, CancellationToken ct = default);
}

public sealed class CaseService : ICaseService
{
    private readonly ICaseRepository _repo;
    private readonly IStorageService _storage;
    private readonly IReportService _reports;
    private readonly ILogger<CaseService> _log;

    public CaseService(ICaseRepository repo, IStorageService storage,
        IReportService reports, ILogger<CaseService> log)
    {
        _repo = repo;
        _storage = storage;
        _reports = reports;
        _log = log;
    }

    public Task<List<Case>> ListAsync(string officerDni, CancellationToken ct = default) =>
        _repo.ListByOfficerAsync(officerDni, ct);

    public async Task<Result<(Case Case, List<FileInfoDto> Files)>> GetAsync(
        string id, string officerDni, CancellationToken ct = default)
    {
        var cas = await _repo.FindByIdAsync(id, ct);
        if (cas is null) return Result.Fail<(Case, List<FileInfoDto>)>("Caso no encontrado");
        if (cas.Officer.Dni != officerDni) return Result.Fail<(Case, List<FileInfoDto>)>("Acceso denegado");

        var files = await _storage.ListFilesAsync(cas.Id);
        return Result.Ok((cas, MergeSources(files, cas)));
    }

    public async Task<Case> CreateAsync(CreateCaseRequest request, User officer,
        CancellationToken ct = default)
    {
        var cas = new Case
        {
            Id = Guid.NewGuid().ToString(),
            NroReferencia = request.NroReferencia,
            NombreDenunciante = request.NombreDenunciante,
            DniDenunciante = request.DniDenunciante,
            Observaciones = request.Observaciones,
            Officer = officer,
            Device = request.Device is { } d ? new DeviceInfo
            {
                Serial = d.Serial, Manufacturer = d.Manufacturer, Model = d.Model,
                AndroidVersion = d.AndroidVersion, Imei = d.Imei,
                Platform = d.Platform, OsVersion = d.OsVersion
            } : new DeviceInfo(),
            Status = CaseStatus.Draft,
            CreatedAt = DateTime.UtcNow
        };

        _storage.CaseDir(cas.Id);
        await _repo.InsertAsync(cas, ct);
        return cas;
    }

    public async Task<Result<FileInfoDto>> UploadFileAsync(string id, string officerDni,
        string filename, string? sourcePath, Stream content, CancellationToken ct = default)
    {
        var cas = await _repo.FindByIdAsync(id, ct);
        if (cas is null) return Result.Fail<FileInfoDto>("Caso no encontrado");
        if (cas.Officer.Dni != officerDni) return Result.Fail<FileInfoDto>("Acceso denegado");

        var (path, hash) = await _storage.SaveFileAsync(id, filename, content, ct);
        var info = new FileInfo(path);

        if (!string.IsNullOrWhiteSpace(sourcePath))
            await _repo.AddFileSourceAsync(id, info.Name, sourcePath, ct);

        return Result.Ok(new FileInfoDto(info.Name, info.Length, hash, info.LastWriteTimeUtc, sourcePath));
    }

    public async Task<Result<List<FileInfoDto>>> ListFilesAsync(string id, string officerDni,
        CancellationToken ct = default)
    {
        var cas = await _repo.FindByIdAsync(id, ct);
        if (cas is null) return Result.Fail<List<FileInfoDto>>("Caso no encontrado");
        if (cas.Officer.Dni != officerDni) return Result.Fail<List<FileInfoDto>>("Acceso denegado");

        var files = await _storage.ListFilesAsync(id);
        return Result.Ok(MergeSources(files, cas));
    }

    // Los archivos se listan escaneando el directorio del caso (StorageService no sabe nada
    // de metadata) — la ruta de origen en el dispositivo vive en el Case de Mongo y se pega acá.
    private static List<FileInfoDto> MergeSources(List<FileInfoDto> files, Case cas)
    {
        if (cas.FileSources.Count == 0) return files;
        var map = cas.FileSources.ToDictionary(s => s.Filename, s => s.SourcePath);
        return files.Select(f => map.TryGetValue(f.Name, out var src)
            ? f with { SourcePath = src } : f).ToList();
    }

    public async Task<Result<GenerateResponse>> GenerateAsync(string id, string officerDni,
        CancellationToken ct = default)
    {
        var cas = await _repo.FindByIdAsync(id, ct);
        if (cas is null) return Result.Fail<GenerateResponse>("Caso no encontrado");
        if (cas.Officer.Dni != officerDni) return Result.Fail<GenerateResponse>("Acceso denegado");

        // ListFilesAsync escanea el directorio completo del caso — si un intento de generación
        // anterior falló a mitad de camino, el ZIP/DOCX que llegó a crear antes de fallar queda
        // en esa misma carpeta y se listaría acá como si fuera evidencia. Sin este filtro, un
        // reintento re-empaqueta el ZIP/DOCX de la vez anterior como "evidencia" (incluido el
        // propio ZIP dentro de sí mismo), lo que corrompe el paquete y puede hacer que
        // CreateZipAsync intente leer el mismo archivo que está escribiendo.
        var files = MergeSources(await _storage.ListFilesAsync(id), cas)
            .Where(f => !ReportService.IsGeneratedArtifact(f.Name))
            .ToList();
        if (files.Count == 0)
            return Result.Fail<GenerateResponse>("El caso no tiene archivos. Capturá evidencia primero.");

        await _repo.UpdateStatusAsync(id, CaseStatus.Generating, ct);
        _log.LogInformation("Generando informe para caso {CaseId}", id);

        try
        {
            var caseDir = _storage.CaseDir(id);
            var result = await _reports.GenerateAsync(cas, files, caseDir, ct);
            var now = DateTime.UtcNow;

            await _repo.UpdateGeneratedAsync(id, now, result.Password, result.ZipHash,
                result.ZipFilename, result.PdfFilename, ct);

            cas = (await _repo.FindByIdAsync(id, ct))!;
            return Result.Ok(new GenerateResponse(
                cas, result.ZipHash, result.Password,
                new FilesDto(result.ZipFilename, result.PdfFilename)));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error generando informe para caso {CaseId}", id);
            await _repo.UpdateStatusAsync(id, CaseStatus.Error, ct);
            return Result.Fail<GenerateResponse>($"Error generando informe: {ex.Message}");
        }
    }

    public async Task<Result<(string Path, string ContentType, string FileName)>> DownloadAsync(
        string id, string filename, string officerDni, CancellationToken ct = default)
    {
        var cas = await _repo.FindByIdAsync(id, ct);
        if (cas is null) return Result.Fail<(string, string, string)>("Caso no encontrado");
        if (cas.Officer.Dni != officerDni) return Result.Fail<(string, string, string)>("Acceso denegado");

        var caseDir = _storage.CaseDir(id);
        var path = Path.Combine(caseDir, filename);
        if (!File.Exists(path)) return Result.Fail<(string, string, string)>("Archivo no encontrado");

        var contentType = Path.GetExtension(filename).ToLower() switch
        {
            ".zip" => "application/zip",
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => "application/octet-stream"
        };

        return Result.Ok((path, contentType, filename));
    }
}
