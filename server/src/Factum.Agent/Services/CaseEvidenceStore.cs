using System.Buffers;
using System.Security.Cryptography;
using Factum.Agent.Common;
using Factum.Agent.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Services;

/// <summary>Archivo guardado en la carpeta del caso: <c>{ "filename", "size", "sha256", "saved_at" }</c>.</summary>
public sealed record EvidenceFileInfo(string Filename, long Size, string Sha256, DateTime SavedAt);

/// <summary>Entrada de <c>GET /cases/{id}/files</c> (sin hash).</summary>
public sealed record CaseFileEntry(string Filename, long Size, DateTime ModifiedAt);

/// <summary><c>{ "directory", "files": [ … ] }</c>.</summary>
public sealed record CaseFilesListing(string Directory, List<CaseFileEntry> Files);

/// <summary>
/// Error con código estable (<see cref="AgentErrorCodes"/>) que el controlador traduce a
/// <c>{ "error", "code", …Extra }</c> con <see cref="Status"/>. El mensaje es legible y nunca
/// incluye una contraseña.
/// </summary>
public sealed class AgentHttpException(int status, string code, string message,
    IReadOnlyDictionary<string, object?>? extra = null, Exception? inner = null) : Exception(message, inner)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, object?>? Extra { get; } = extra;

    public Dictionary<string, object?> ToBody()
    {
        var body = new Dictionary<string, object?> { ["error"] = Message, ["code"] = Code };
        if (Extra is not null)
            foreach (var (k, v) in Extra)
                if (v is not null) body[k] = v;
        return body;
    }
}

/// <summary>Sondeo de espacio libre (fail-open: null si no se pudo).</summary>
public interface IAgentDiskProbe
{
    long? GetAvailableFreeBytes(string path);
}

public sealed class DriveInfoAgentDiskProbe : IAgentDiskProbe
{
    public long? GetAvailableFreeBytes(string path)
    {
        try { return new DriveInfo(Path.GetFullPath(path)).AvailableFreeSpace; }
        catch { return null; }
    }
}

/// <summary>
/// Carpeta de trabajo de cada caso en la PC del perito (zip-local-informe-servidor §3.4 y §6.2):
/// <c>&lt;DataDirectory&gt;/cases/&lt;caseId&gt;/</c>. Los archivos de la raíz plana se MUEVEN acá
/// (rename, mismo volumen) y los del navegador se suben con staging atómico.
/// </summary>
public interface ICaseEvidenceStore
{
    string DataDirectory { get; }
    /// <summary>Ruta absoluta de la carpeta del caso (no la crea). Valida el id.</summary>
    string CaseDir(string caseId);
    Task<EvidenceFileInfo> ImportAsync(string caseId, string? filename, CancellationToken ct);
    /// <summary>Prechequeo de subida; devuelve <c>max_upload_bytes</c>.</summary>
    long CheckUpload(string caseId, string? filename, long? size);
    Task<EvidenceFileInfo> StageAndCommitUploadAsync(string caseId, string? filename, long? contentLength,
        Stream body, CancellationToken ct);
    CaseFilesListing List(string caseId);
    /// <summary>Ruta del archivo del caso si existe; null si no.</summary>
    string? TryGetFile(string caseId, string? filename);
    bool Delete(string caseId, string? filename);
    /// <summary>Borra solo los archivos de <c>.upload-tmp/</c>. Al arrancar.</summary>
    int CleanupOrphanUploads();
    Task<string> Sha256Async(string path, CancellationToken ct, Action<long>? onProgress = null);
}

public sealed class CaseEvidenceStore : ICaseEvidenceStore
{
    internal const string UploadTmpDirName = ".upload-tmp";
    private const int CopyBufferSize = 1024 * 1024;

    private readonly string _root;
    private readonly string _casesRoot;
    private readonly string _uploadTmp;
    private readonly long _maxUploadBytes;
    private readonly long _minFreeBytes;
    private readonly IAgentDiskProbe _probe;
    private readonly ILogger _log;

    public CaseEvidenceStore(IOptions<AgentOptions> opts, IAgentDiskProbe probe, ILogger<CaseEvidenceStore> log)
        : this(opts.Value, probe, log)
    {
    }

    internal CaseEvidenceStore(AgentOptions opts, IAgentDiskProbe? probe = null, ILogger? log = null)
    {
        _root = Path.GetFullPath(opts.DataDirectory);
        _casesRoot = Path.Combine(_root, "cases");
        _uploadTmp = Path.Combine(_root, UploadTmpDirName);
        _maxUploadBytes = opts.MaxUploadBytes;
        _minFreeBytes = opts.MinFreeBytes;
        _probe = probe ?? new DriveInfoAgentDiskProbe();
        _log = log ?? NullLogger.Instance;
        Directory.CreateDirectory(_root);
    }

    public string DataDirectory => _root;

    // ── Validaciones ─────────────────────────────────────────────────────────

    internal static string RequireCaseId(string? caseId) =>
        AgentFileNames.TryParseCaseId(caseId, out var canonical)
            ? canonical
            : throw new AgentHttpException(400, AgentErrorCodes.InvalidCaseId, "Identificador de caso inválido");

    internal static string RequireName(string? filename) =>
        AgentFileNames.IsValidName(filename)
            ? filename!
            : throw new AgentHttpException(400, AgentErrorCodes.InvalidFilename,
                "Nombre de archivo inválido: no puede tener rutas ni los caracteres < > : \" | ? *");

    public string CaseDir(string caseId) => Path.Combine(_casesRoot, RequireCaseId(caseId));

    // Defensa en profundidad: el archivo resuelto tiene que estar exactamente en la carpeta.
    private static string Child(string dir, string name) =>
        AgentFileNames.SafeChild(dir, name)
        ?? throw new AgentHttpException(400, AgentErrorCodes.InvalidFilename, "El nombre del archivo sale de la carpeta del caso");

    // ── Importar desde la raíz plana ─────────────────────────────────────────

    public async Task<EvidenceFileInfo> ImportAsync(string caseId, string? filename, CancellationToken ct)
    {
        var dir = CaseDir(caseId);
        var name = RequireName(filename);
        var source = Child(_root, name);
        var dest = Child(dir, name);

        if (File.Exists(source))
        {
            if (File.Exists(dest))
                throw new AgentHttpException(409, AgentErrorCodes.FileExists,
                    $"Ya hay un archivo {name} en la carpeta del caso");
            try
            {
                Directory.CreateDirectory(dir);
                // Mismo volumen: rename atómico, no duplica GB. overwrite:false: nunca pisa.
                File.Move(source, dest, overwrite: false);
            }
            catch (IOException ex) when (File.Exists(source) && !File.Exists(dest))
            {
                // Archivo en uso (típico en Windows mientras ffmpeg genera variantes).
                throw new AgentHttpException(409, AgentErrorCodes.FileBusy,
                    $"{name} todavía se está procesando; esperá unos segundos y volvé a intentar", inner: ex);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new AgentHttpException(500, AgentErrorCodes.StorageError,
                    $"No se pudo mover {name} a la carpeta del caso", inner: ex);
            }
            _log.LogInformation("Evidencia {Name} movida a la carpeta del caso {CaseId}", name, Path.GetFileName(dir));
        }
        else if (!File.Exists(dest))
        {
            throw new AgentHttpException(404, AgentErrorCodes.FileNotFound, $"El archivo {name} no existe");
        }

        string hash;
        try
        {
            hash = await Sha256Async(dest, ct);
        }
        catch (IOException ex)
        {
            throw new AgentHttpException(409, AgentErrorCodes.FileBusy,
                $"{name} todavía se está procesando; esperá unos segundos y volvé a intentar", inner: ex);
        }
        var info = new FileInfo(dest);
        return new EvidenceFileInfo(name, info.Length, hash, DateTime.UtcNow);
    }

    // ── Subida del navegador (mismas reglas que el backend, subida-archivos-grandes §4.1) ─

    public long CheckUpload(string caseId, string? filename, long? size)
    {
        CaseDir(caseId);
        var name = RequireName(filename);
        if (size is not { } length || length < 0)
            throw new AgentHttpException(400, AgentErrorCodes.LengthRequired, "Falta el tamaño del archivo (Content-Length)");
        if (length > _maxUploadBytes)
            throw new AgentHttpException(413, AgentErrorCodes.FileTooLarge,
                $"El archivo {name} pesa {FormatBytes(length)} y el máximo que acepta Tatana es {FormatBytes(_maxUploadBytes)}",
                new Dictionary<string, object?> { ["size"] = length, ["max_upload_bytes"] = _maxUploadBytes });
        if (_probe.GetAvailableFreeBytes(_root) is { } available)
        {
            var required = length + _minFreeBytes;
            if (available < required)
                throw InsufficientStorage(name, length, required, available);
        }
        return _maxUploadBytes;
    }

    private static AgentHttpException InsufficientStorage(string name, long? size, long required, long? available,
        Exception? inner = null) =>
        new(507, AgentErrorCodes.InsufficientStorage,
            available is { } a
                ? $"No hay espacio en el disco de esta PC para guardar {name} (hace falta {FormatBytes(required)}, quedan {FormatBytes(a)})"
                : $"No hay espacio en el disco de esta PC para guardar {name}",
            new Dictionary<string, object?>
            {
                ["size"] = size, ["required_bytes"] = required, ["available_bytes"] = available,
            }, inner);

    public async Task<EvidenceFileInfo> StageAndCommitUploadAsync(string caseId, string? filename,
        long? contentLength, Stream body, CancellationToken ct)
    {
        CheckUpload(caseId, filename, contentLength);
        var dir = CaseDir(caseId);
        var name = filename!;
        var expected = contentLength!.Value;
        var finalPath = Child(dir, name);
        var canonicalId = Path.GetFileName(dir);
        var tempPath = Path.GetFullPath(Path.Combine(_uploadTmp, $"{canonicalId}.{Guid.NewGuid():N}.part"));
        if (Path.GetDirectoryName(tempPath) != _uploadTmp)
            throw new AgentHttpException(400, AgentErrorCodes.InvalidCaseId, "Identificador de caso inválido");

        Directory.CreateDirectory(_uploadTmp);
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        var committed = false;
        try
        {
            string hash;
            long received = 0;
            await using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             bufferSize: 1, FileOptions.Asynchronous))
            {
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                while (true)
                {
                    int read;
                    try
                    {
                        read = await body.ReadAsync(buffer.AsMemory(0, CopyBufferSize), ct);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        throw Incomplete(name, expected, received, ex);
                    }
                    if (read == 0) break;
                    if (received + read > expected) throw Incomplete(name, expected, received + read);
                    try
                    {
                        await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                    }
                    catch (IOException ex) when (IsDiskFull(ex, expected - received))
                    {
                        throw InsufficientStorage(name, expected, expected + _minFreeBytes,
                            _probe.GetAvailableFreeBytes(_root), ex);
                    }
                    sha.AppendData(buffer, 0, read);
                    received += read;
                }
                if (received != expected) throw Incomplete(name, expected, received);
                // fsync antes del rename: el nombre final nunca apunta a datos a medio bajar.
                fs.Flush(flushToDisk: true);
                hash = Convert.ToHexStringLower(sha.GetHashAndReset());
            }

            Directory.CreateDirectory(dir);
            File.Move(tempPath, finalPath, overwrite: true);
            committed = true;
            var info = new FileInfo(finalPath);
            return new EvidenceFileInfo(name, info.Length, hash, DateTime.UtcNow);
        }
        catch (AgentHttpException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new AgentHttpException(500, AgentErrorCodes.StorageError,
                $"Tatana no pudo guardar {name}; no se guardó nada", inner: ex);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            if (!committed)
            {
                try { File.Delete(tempPath); }
                catch (Exception ex) { _log.LogWarning(ex, "No se pudo borrar el temporal {Temp}", tempPath); }
            }
        }
    }

    private static AgentHttpException Incomplete(string name, long expected, long received, Exception? inner = null) =>
        new(400, AgentErrorCodes.IncompleteUpload,
            $"Se cortó la copia de {name} a la carpeta del caso ({FormatBytes(received)} de {FormatBytes(expected)}); no se guardó nada",
            new Dictionary<string, object?> { ["size"] = expected, ["received_bytes"] = received }, inner);

    private bool IsDiskFull(IOException ex, long remaining)
    {
        if (ex.HResult is 28 or unchecked((int)0x80070070) or unchecked((int)0x80070027)) return true;
        return _probe.GetAvailableFreeBytes(_root) is { } f && f < remaining;
    }

    // ── Listado, lectura y borrado ───────────────────────────────────────────

    public CaseFilesListing List(string caseId)
    {
        var dir = CaseDir(caseId);
        if (!Directory.Exists(dir)) return new CaseFilesListing(dir, []);
        var files = Directory.EnumerateFiles(dir)
            .Select(p => new FileInfo(p))
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .Select(f => new CaseFileEntry(f.Name, f.Length, f.LastWriteTimeUtc))
            .ToList();
        return new CaseFilesListing(dir, files);
    }

    public string? TryGetFile(string caseId, string? filename)
    {
        var dir = CaseDir(caseId);
        var path = Child(dir, RequireName(filename));
        return File.Exists(path) ? path : null;
    }

    public bool Delete(string caseId, string? filename)
    {
        var dir = CaseDir(caseId);
        var path = Child(dir, RequireName(filename));
        if (!File.Exists(path)) return false;
        try
        {
            File.Delete(path);
        }
        catch (IOException ex)
        {
            throw new AgentHttpException(409, AgentErrorCodes.FileBusy,
                $"{filename} está en uso; esperá unos segundos y volvé a intentar", inner: ex);
        }
        return true;
    }

    public int CleanupOrphanUploads()
    {
        if (!Directory.Exists(_uploadTmp)) return 0;
        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(_uploadTmp))
        {
            try { File.Delete(file); deleted++; }
            catch (Exception ex) { _log.LogWarning(ex, "No se pudo borrar el temporal {Temp}", file); }
        }
        return deleted;
    }

    public async Task<string> Sha256Async(string path, CancellationToken ct, Action<long>? onProgress = null)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            int read;
            while ((read = await fs.ReadAsync(buffer.AsMemory(0, CopyBufferSize), ct)) > 0)
            {
                sha.AppendData(buffer, 0, read);
                onProgress?.Invoke(read);
            }
            return Convert.ToHexStringLower(sha.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Base 1024, coma decimal (mismas reglas que el backend y <c>client/src/lib/format.ts</c>).</summary>
    internal static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return bytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + " B";
        string[] units = ["KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = -1;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        var rounded = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        return rounded.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',') + " " + units[unit];
    }
}
