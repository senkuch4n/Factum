using System.Buffers;
using System.Security.Cryptography;
using Factum.Backend.DTOs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Factum.Backend.Infrastructure;

public sealed class StorageOptions
{
    public string DataDirectory { get; set; } = "./data";
    /// <summary>Tope de <c>POST /api/cases/{id}/files</c> (solo ese endpoint). Default 4 GiB.</summary>
    public long MaxUploadBytes { get; set; } = 4L * 1024 * 1024 * 1024;
    /// <summary>Margen libre que tiene que quedar en el disco después de una subida. Default 1 GiB.</summary>
    public long MinFreeBytes { get; set; } = 1L * 1024 * 1024 * 1024;
}

public interface IStorageService
{
    string CaseDir(string caseId);
    Task<List<FileInfoDto>> ListFilesAsync(string caseId);
    Task<string> Sha256Async(string path);
    void DeleteCaseFiles(string caseId);

    /// <summary>Espacio libre del volumen de <c>DataDirectory</c>, o <c>null</c> si no se pudo sondear.</summary>
    long? GetAvailableFreeBytes();

    /// <summary>
    /// Escribe el cuerpo a un temporal en <c>&lt;DataDirectory&gt;/.upload-tmp/</c> (fuera del caso),
    /// verifica que lleguen exactamente <paramref name="expectedLength"/> bytes, hace fsync y devuelve
    /// la subida lista para <see cref="StagedUpload.Commit"/>. Ante cualquier excepción borra el temporal.
    /// </summary>
    /// <exception cref="UploadIncompleteException">Cuerpo corto, largo o cortado al leerlo.</exception>
    /// <exception cref="InsufficientStorageException">Disco lleno durante la escritura.</exception>
    /// <exception cref="ArgumentException">El nombre final escaparía de la carpeta del caso.</exception>
    Task<StagedUpload> StageUploadAsync(string caseId, string filename, Stream content,
        long expectedLength, CancellationToken ct);

    /// <summary>Borra los archivos que haya en <c>.upload-tmp/</c> (solo ahí, sin recursión). Solo al arrancar.</summary>
    int CleanupOrphanUploads();

    // ── zip-local-informe-servidor (§3.3, §5.8) ──────────────────────────────

    /// <summary>
    /// true si <c>cases/&lt;id&gt;/</c> existe y tiene al menos un archivo que no es un artefacto
    /// generado. Solo lectura: NO crea la carpeta.
    /// </summary>
    bool HasEvidenceFiles(string caseId);

    /// <summary>
    /// Carpeta vacía <c>&lt;DataDirectory&gt;/.generate-tmp/&lt;generationId&gt;/</c> (si existía,
    /// se borra antes). <paramref name="generationId"/> tiene que ser un Guid "N".
    /// </summary>
    string NewGenerationTempDir(string generationId);

    /// <summary>Borra todo lo que haya DENTRO de <c>.generate-tmp/</c>. Solo al arrancar.</summary>
    int CleanupOrphanGenerations();
}

public sealed class StorageService : IStorageService
{
    internal const string UploadTmpDirName = ".upload-tmp";
    internal const string GenerateTmpDirName = ".generate-tmp";
    private const int CopyBufferSize = 1024 * 1024; // DT16

    private readonly string _root;
    private readonly string _uploadTmp;
    private readonly string _generateTmp;
    private readonly long _minFreeBytes;
    private readonly IDiskSpaceProbe _probe;
    private readonly Func<string, Stream> _openTempForWrite;
    private readonly ILogger _log;

    public StorageService(IOptions<StorageOptions> opts, IDiskSpaceProbe probe, ILogger<StorageService> log)
        : this(opts.Value, probe, null, log)
    {
    }

    /// <summary>Para tests: permite inyectar el stream del temporal (disco lleno simulado, etc.).</summary>
    internal StorageService(StorageOptions opts, IDiskSpaceProbe probe, Func<string, Stream>? openTempForWrite,
        ILogger? log = null)
    {
        _root = Path.GetFullPath(opts.DataDirectory);
        _uploadTmp = Path.Combine(_root, UploadTmpDirName);
        _generateTmp = Path.Combine(_root, GenerateTmpDirName);
        _minFreeBytes = opts.MinFreeBytes;
        _probe = probe;
        _log = log ?? NullLogger.Instance;
        // bufferSize 1 = sin buffer interno: se escribe con un buffer propio de 1 MiB.
        _openTempForWrite = openTempForWrite ?? (path => new FileStream(path, FileMode.CreateNew,
            FileAccess.Write, FileShare.None, bufferSize: 1, FileOptions.Asynchronous));
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "cases"));
    }

    public string CaseDir(string caseId)
    {
        var dir = Path.Combine(_root, "cases", caseId);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public Task<List<FileInfoDto>> ListFilesAsync(string caseId)
    {
        var dir = CaseDir(caseId);
        var files = Directory.EnumerateFiles(dir)
            .Select(p =>
            {
                var info = new FileInfo(p);
                return new FileInfoDto(info.Name, info.Length, string.Empty, info.LastWriteTimeUtc);
            })
            .ToList();
        return Task.FromResult(files);
    }

    public async Task<string> Sha256Async(string path)
    {
        using var sha = SHA256.Create();
        await using var fs = File.OpenRead(path);
        var hash = await sha.ComputeHashAsync(fs);
        return Convert.ToHexStringLower(hash);
    }

    public void DeleteCaseFiles(string caseId)
    {
        var dir = Path.Combine(_root, "cases", caseId);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    // ── Subidas (subida-archivos-grandes §5.3) ──────────────────────────────

    public long? GetAvailableFreeBytes() => _probe.GetAvailableFreeBytes(_root);

    public async Task<StagedUpload> StageUploadAsync(string caseId, string filename, Stream content,
        long expectedLength, CancellationToken ct)
    {
        // Defensa en profundidad: el nombre ya llega validado, pero nada se crea si el destino
        // (o el temporal) quedara fuera de su carpeta.
        var caseDir = Path.GetFullPath(Path.Combine(_root, "cases", caseId));
        var finalPath = Path.GetFullPath(Path.Combine(caseDir, filename));
        if (!finalPath.StartsWith(caseDir + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.GetDirectoryName(finalPath) != caseDir)
            throw new ArgumentException("El nombre del archivo sale de la carpeta del caso", nameof(filename));
        var tempPath = Path.GetFullPath(Path.Combine(_uploadTmp, $"{caseId}.{Guid.NewGuid():N}.part"));
        if (Path.GetDirectoryName(tempPath) != _uploadTmp)
            throw new ArgumentException("Identificador de caso inválido", nameof(caseId));

        Directory.CreateDirectory(_uploadTmp);
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        Stream? stream = null;
        var success = false;
        try
        {
            stream = _openTempForWrite(tempPath);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long received = 0;

            while (true)
            {
                int read;
                try
                {
                    read = await content.ReadAsync(buffer.AsMemory(0, CopyBufferSize), ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Corte de red, BadHttpRequestException de Kestrel, IOException del cliente…
                    throw new UploadIncompleteException(received, expectedLength, ex);
                }
                if (read == 0) break;

                if (received + read > expectedLength)
                    throw new UploadIncompleteException(received + read, expectedLength);

                try
                {
                    await stream.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                catch (IOException ex) when (IsDiskFull(ex, expectedLength - received))
                {
                    throw new InsufficientStorageException(expectedLength + _minFreeBytes,
                        GetAvailableFreeBytes(), ex);
                }
                sha.AppendData(buffer, 0, read);
                received += read;
            }

            if (received != expectedLength)
                throw new UploadIncompleteException(received, expectedLength);

            try
            {
                // DT5: fsync antes del rename para que el nombre final nunca apunte a datos a medio bajar.
                if (stream is FileStream fs) fs.Flush(flushToDisk: true);
                else await stream.FlushAsync(CancellationToken.None);
                await stream.DisposeAsync();
                stream = null;
            }
            catch (IOException ex) when (IsDiskFull(ex, 0))
            {
                throw new InsufficientStorageException(expectedLength + _minFreeBytes, GetAvailableFreeBytes(), ex);
            }

            var hash = Convert.ToHexStringLower(sha.GetHashAndReset());
            success = true;
            return new StagedUpload(tempPath, finalPath, caseDir, received, hash, LogDeleteFailed);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            if (stream is not null)
            {
                try { await stream.DisposeAsync(); }
                catch (Exception ex) { _log.LogDebug(ex, "Error cerrando el temporal {Temp}", tempPath); }
            }
            if (!success)
            {
                try { File.Delete(tempPath); }
                catch (Exception ex) { LogDeleteFailed(tempPath, ex); }
            }
        }
    }

    // ENOSPC (28) en Linux/macOS, ERROR_DISK_FULL / ERROR_HANDLE_DISK_FULL en Windows; si el código
    // no lo dice, el sondeo posterior decide (queda menos libre que lo que faltaba escribir).
    private bool IsDiskFull(IOException ex, long remaining)
    {
        if (ex.HResult is 28 or unchecked((int)0x80070070) or unchecked((int)0x80070027)) return true;
        var free = GetAvailableFreeBytes();
        return free is { } f && f < remaining;
    }

    private void LogDeleteFailed(string path, Exception ex) =>
        _log.LogWarning(ex, "No se pudo borrar el temporal de subida {Temp}", path);

    // ── zip-local-informe-servidor ───────────────────────────────────────────

    public bool HasEvidenceFiles(string caseId)
    {
        if (string.IsNullOrEmpty(caseId) || !Factum.Backend.Services.Reports.ReportImageRef.IsPlainName(caseId))
            return false;
        var dir = Path.Combine(_root, "cases", caseId);
        if (!Directory.Exists(dir)) return false;
        try
        {
            return Directory.EnumerateFiles(dir)
                .Any(p => !Factum.Backend.Services.Reports.ReportService.IsGeneratedArtifact(Path.GetFileName(p)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "No se pudo leer la carpeta del caso {CaseId}", caseId);
            return false;
        }
    }

    public string NewGenerationTempDir(string generationId)
    {
        if (!Guid.TryParseExact(generationId, "N", out _))
            throw new ArgumentException("Identificador de generación inválido", nameof(generationId));
        var dir = Path.GetFullPath(Path.Combine(_generateTmp, generationId));
        if (Path.GetDirectoryName(dir) != _generateTmp)
            throw new ArgumentException("Identificador de generación inválido", nameof(generationId));
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public int CleanupOrphanGenerations()
    {
        if (!Directory.Exists(_generateTmp)) return 0;

        var deleted = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(_generateTmp))
        {
            try
            {
                if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
                else File.Delete(entry);
                deleted++;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "No se pudo borrar el temporal de generación {Temp}", entry);
            }
        }
        return deleted;
    }

    public int CleanupOrphanUploads()
    {
        if (!Directory.Exists(_uploadTmp))
        {
            Directory.CreateDirectory(_uploadTmp);
            return 0;
        }

        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(_uploadTmp))
        {
            try
            {
                File.Delete(file);
                deleted++;
            }
            catch (Exception ex)
            {
                LogDeleteFailed(file, ex);
            }
        }
        return deleted;
    }
}
