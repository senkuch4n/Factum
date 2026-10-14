using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Factum.Agent.Common;
using Factum.Agent.Models;
using Factum.Agent.Services.Evidence;
using Factum.Agent.Services.Session;
using Factum.Agent.WebSockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Services;

// ── Contrato (zip-local-informe-servidor §6.3 y §8.2). JSON snake_case, null omitido. ──

/// <summary>
/// Un archivo del ZIP. <c>zip_folder</c> (grabacion-sesion-completa §5.13): null = entrada plana; <c>"sesion"</c> = la
/// entrada va como <c>sesion/&lt;filename&gt;</c> (solo tramos de la grabación de la sesión).
/// </summary>
public sealed record ZipFileItem(string? Filename = null, long? Size = null, string? Sha256 = null,
    string? ZipFolder = null);

public sealed record BuildZipRequest(string? CaseRef = null, string? ZipFilename = null, string? Password = null,
    List<ZipFileItem>? Files = null);

public sealed record BuildZipResponse(string ZipFilename, string ZipHash, long ZipSize, bool Encrypted,
    string? Encryption, string Directory, string ZipPath, string Hostname, List<ZipFileItem> Files);

public sealed record CommitZipRequest(string? CaseRef = null, string? ZipFilename = null, string? ZipHash = null,
    List<string>? DeleteFiles = null);

public sealed record CommitZipResponse(string ZipPath, int Deleted);

public sealed record ZipStatusResponse(string State, string ZipPath, string Directory, string? PendingHash,
    string? CommittedHash);

// ── sellado-tiempo §7.3: PUT /cases/{caseId}/zip/seals (copia de los .tsr y del LEEME a sellos/). ──

/// <summary>Un sello: <c>{ "name": "captura_1.png.tsr", "content_base64": "MIIS…" }</c>.</summary>
public sealed record SealFileItem(string? Name = null, string? ContentBase64 = null);

/// <summary>Pedido de <c>PUT …/zip/seals</c>. <c>readme</c> null o ausente → no se escribe el LEEME.</summary>
public sealed record WriteSealsRequest(string? CaseRef = null, string? ZipFilename = null,
    List<SealFileItem>? Files = null, string? Readme = null);

/// <summary><c>{ "directory": "&lt;ruta absoluta de sellos/&gt;", "written": 3, "unchanged": 1 }</c>.</summary>
public sealed record WriteSealsResponse(string Directory, int Written, int Unchanged);

/// <summary>Motivo de <c>evidence_changed</c>: <c>{ "filename", "reason": "missing" | "size" | "hash" }</c>.</summary>
public sealed record EvidenceChange(string Filename, string Reason);

/// <summary><c>estado.json</c> de la carpeta final, escrito en el commit.</summary>
internal sealed record ZipCommitState(string ZipFilename, string ZipHash, DateTime CommittedAt);

public interface ICaseZipService
{
    /// <summary>Carpeta base resuelta (<c>Agent:EvidenceDirectory</c> o el default por SO).</summary>
    string EvidenceDirectory { get; }
    Task<BuildZipResponse> BuildAsync(string caseId, BuildZipRequest request, CancellationToken ct);
    Task<CommitZipResponse> CommitAsync(string caseId, CommitZipRequest request, CancellationToken ct);
    void DiscardPending(string caseId, string? caseRef, string? zipFilename);
    ZipStatusResponse Status(string caseId, string? caseRef, string? zipFilename);
    /// <summary>Ruta del ZIP final si existe; null si no.</summary>
    string? FinalZipPath(string caseId, string? caseRef, string? zipFilename);
    /// <summary>
    /// Al arrancar (tatana-ws-y-hosts T-05): borra los <c>evidencia_*.zip.part</c> de
    /// <c>&lt;EvidenceDirectory&gt;/*/.factum/pendiente/</c>. Nunca carpetas, <c>.zip</c> ni <c>.sha256</c>.
    /// Devuelve cuántos borró.
    /// </summary>
    int CleanupOrphanParts();
    /// <summary>
    /// sellado-tiempo §7.3: escribe los <c>.tsr</c> y <c>LEEME-verificacion.txt</c> en <c>&lt;final&gt;/sellos/</c>,
    /// de forma atómica e idempotente (mismos bytes → <c>unchanged</c>). Requiere el ZIP final en esta PC.
    /// </summary>
    Task<WriteSealsResponse> WriteSealsAsync(string caseId, WriteSealsRequest request, CancellationToken ct);
}

/// <summary>
/// ZIP del caso en la PC del perito (§6.3): verificación contra el manifiesto, ZIP provisorio en
/// <c>&lt;final&gt;/.factum/pendiente/</c>, verificación del ZIP, y commit (rename) recién cuando el
/// backend registró el hash. Un solo ZIP por caso a la vez.
/// </summary>
public sealed class CaseZipService : ICaseZipService
{
    internal const string MetaDirName = ".factum";
    internal const string PendingDirName = "pendiente";
    internal const string StateFileName = "estado.json";
    internal const string SealsDirName = "sellos";
    internal const string SealsReadmeName = "LEEME-verificacion.txt";
    internal const int MaxSealFiles = 1000;
    internal const int MaxSealBytes = 64 * 1024;
    internal const int MaxReadmeBytes = 256 * 1024;
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(500);
    /// <summary>Tope de la contraseña en bytes UTF-8: 7-Zip no abre un ZIP AES con una más larga (T-09).</summary>
    internal const int MaxPasswordBytes = 99;

    private static readonly JsonSerializerOptions StateJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    private readonly ICaseEvidenceStore _store;
    private readonly AgentWebSocketHub? _hub;
    private readonly IAgentDiskProbe _probe;
    private readonly ILogger _log;
    private readonly long _minFreeBytes;
    private readonly SessionJournal? _sessions;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public CaseZipService(IOptions<AgentOptions> opts, ICaseEvidenceStore store, AgentWebSocketHub hub,
        IAgentDiskProbe probe, ILogger<CaseZipService> log, SessionJournal sessions)
        : this(opts.Value, store, hub, probe, log, sessions)
    {
    }

    internal CaseZipService(AgentOptions opts, ICaseEvidenceStore store, AgentWebSocketHub? hub = null,
        IAgentDiskProbe? probe = null, ILogger? log = null, SessionJournal? sessions = null)
    {
        _sessions = sessions ?? new SessionJournal(opts.DataDirectory);
        EvidenceDirectory = opts.ResolveEvidenceDirectory();
        _store = store;
        _hub = hub;
        _probe = probe ?? new DriveInfoAgentDiskProbe();
        _log = log ?? NullLogger.Instance;
        _minFreeBytes = opts.MinFreeBytes;
    }

    public string EvidenceDirectory { get; }

    // ── Rutas ────────────────────────────────────────────────────────────────

    private sealed record ZipPaths(string CaseId, string Final, string ZipPath, string PendingDir, string PendingZip,
        string PendingPart, string PendingSha, string StateFile, string ZipFilename);

    private ZipPaths PathsFor(string caseId, string? caseRef, string? zipFilename)
    {
        var id = CaseEvidenceStore.RequireCaseId(caseId);
        if (!AgentFileNames.IsZipFilename(zipFilename))
            throw new AgentHttpException(400, AgentErrorCodes.InvalidFilename, "Nombre de ZIP inválido");
        var final = Path.Combine(EvidenceDirectory, AgentFileNames.CaseFolderName(caseRef, id));
        var meta = Path.Combine(final, MetaDirName);
        var pending = Path.Combine(meta, PendingDirName);
        return new ZipPaths(id, final, Path.Combine(final, zipFilename!), pending,
            Path.Combine(pending, zipFilename!), Path.Combine(pending, zipFilename + ".part"),
            Path.Combine(pending, zipFilename + ".sha256"), Path.Combine(meta, StateFileName), zipFilename!);
    }

    internal SemaphoreSlim LockFor(string caseId) => _locks.GetOrAdd(caseId, _ => new SemaphoreSlim(1, 1));

    // ── POST /cases/{id}/zip ─────────────────────────────────────────────────

    public async Task<BuildZipResponse> BuildAsync(string caseId, BuildZipRequest request, CancellationToken ct)
    {
        var p = PathsFor(caseId, request.CaseRef, request.ZipFilename);
        // T-08/T-09: antes del lock y de tocar disco. null = sin contraseña; " " es válida. Nunca se muestra.
        if (request.Password is { } pw)
        {
            if (pw.Length == 0)
                throw new AgentHttpException(400, AgentErrorCodes.InvalidPassword, "La contraseña del ZIP no puede estar vacía");
            if (Encoding.UTF8.GetByteCount(pw) > MaxPasswordBytes)
                throw new AgentHttpException(400, AgentErrorCodes.InvalidPassword,
                    "La contraseña del ZIP no puede superar los 99 bytes (7-Zip no abriría el ZIP)");
        }
        var files = request.Files ?? [];
        if (files.Count == 0)
            throw new AgentHttpException(400, AgentErrorCodes.InvalidFilename, "No hay archivos para el ZIP");
        var names = new HashSet<string>(StringComparer.Ordinal);
        var entryNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in files)
            if (!AgentFileNames.IsValidName(f.Filename) || !names.Add(f.Filename!) || !IsValidZipFolder(f) ||
                !entryNames.Add(EntryName(f)))
                throw new AgentHttpException(400, AgentErrorCodes.InvalidFilename,
                    $"Nombre de archivo inválido o repetido: {f.Filename}",
                    new Dictionary<string, object?> { ["filename"] = f.Filename });

        var gate = LockFor(p.CaseId);
        if (!await gate.WaitAsync(0, ct))
            throw new AgentHttpException(409, AgentErrorCodes.ZipInProgress, "Ya se está armando el ZIP de este caso en esta PC");
        try
        {
            // 2. Ya confirmado desde esta PC.
            if (ReadState(p) is { } state && state.ZipFilename == p.ZipFilename)
                throw new AgentHttpException(409, AgentErrorCodes.ZipAlreadyCommitted,
                    "El ZIP de este caso ya se generó y se guardó en esta PC");

            // 3. Espacio en el volumen de la carpeta final. T-06: la carpeta todavía no existe (se crea recién al
            // escribir el ZIP), así que se mide sobre el primer ancestro que exista.
            var total = files.Sum(f => Math.Max(0, f.Size ?? 0));
            if (_probe.GetAvailableFreeBytes(ExistingAncestor(p.Final)) is { } available && available < total + _minFreeBytes)
            {
                var required = total + _minFreeBytes;
                throw new AgentHttpException(507, AgentErrorCodes.InsufficientStorage,
                    $"No hay espacio en el disco de esta PC para guardar el ZIP (hace falta {CaseEvidenceStore.FormatBytes(required)}, quedan {CaseEvidenceStore.FormatBytes(available)})",
                    new Dictionary<string, object?> { ["required_bytes"] = required, ["available_bytes"] = available });
            }

            // 4. Verificación de la evidencia contra el manifiesto.
            var caseDir = _store.CaseDir(p.CaseId);
            var progress = new ZipProgress(_hub, p.CaseId, total);
            await progress.PhaseAsync("hashing");
            var changes = new List<EvidenceChange>();
            var paths = new List<string>(files.Count);
            var entries = new List<EvidenceZipEntry>(files.Count);
            var expected = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var f in files)
            {
                var path = Path.Combine(caseDir, f.Filename!);
                if (!File.Exists(path)) { changes.Add(new EvidenceChange(f.Filename!, "missing")); continue; }
                var length = new FileInfo(path).Length;
                if (f.Size != length)
                {
                    changes.Add(new EvidenceChange(f.Filename!, "size"));
                    progress.Add(length);
                    continue;
                }
                var hash = await _store.Sha256Async(path, ct, progress.Add);
                if (!string.Equals(hash, f.Sha256, StringComparison.Ordinal))
                {
                    changes.Add(new EvidenceChange(f.Filename!, "hash"));
                    continue;
                }
                paths.Add(path);
                entries.Add(new EvidenceZipEntry(path, EntryName(f)));
                expected[EntryName(f)] = hash;
            }
            if (changes.Count > 0)
                throw new AgentHttpException(409, AgentErrorCodes.EvidenceChanged,
                    "Algunos archivos de la evidencia cambiaron o faltan en esta PC",
                    new Dictionary<string, object?> { ["files"] = changes });

            // 5-7. ZIP provisorio, hash y verificación. T-06: las carpetas se crean recién acá y, si algo falla,
            // se borran las que creó ESTA llamada y solo si quedaron vacías.
            var meta = Path.Combine(p.Final, MetaDirName);
            var created = new[] { p.Final, meta, p.PendingDir }.Where(d => !Directory.Exists(d)).ToList();
            try
            {
                try
                {
                    Directory.CreateDirectory(p.PendingDir);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new AgentHttpException(500, AgentErrorCodes.StorageError,
                        $"No se pudo crear la carpeta de evidencia {p.Final}", inner: ex);
                }
                DeleteQuietly(p.PendingPart);
                await progress.PhaseAsync("zipping");
                await EvidenceZip.WriteAsync(p.PendingPart, entries, request.Password, ct, progress.Add);
                File.Move(p.PendingPart, p.PendingZip, overwrite: true);
                DeleteQuietly(p.PendingSha);

                var zipSize = new FileInfo(p.PendingZip).Length;
                progress.Reset(zipSize);
                await progress.PhaseAsync("verifying");
                var zipHash = await _store.Sha256Async(p.PendingZip, ct);
                await EvidenceZip.VerifyAsync(p.PendingZip, expected, request.Password, ct);
                await File.WriteAllTextAsync(p.PendingSha, zipHash, ct);
                await progress.DoneAsync();

                _log.LogInformation("ZIP pendiente del caso {CaseId} listo en {Path} ({Size} bytes)",
                    p.CaseId, p.PendingZip, zipSize);
                var encrypted = request.Password is not null;
                return new BuildZipResponse(p.ZipFilename, zipHash, zipSize, encrypted,
                    encrypted ? EvidenceZip.EncryptionAes256Ae2 : null, p.Final, p.ZipPath, Environment.MachineName,
                    files.Select(f => new ZipFileItem(f.Filename, f.Size, f.Sha256, f.ZipFolder)).ToList());
            }
            catch (Exception ex)
            {
                var canceled = ex is OperationCanceledException && ct.IsCancellationRequested;
                // T-07: se clasifica ANTES de borrar el .part (el espacio se mide con el .part todavía ocupándolo).
                var failure = canceled
                    ? null
                    : ex as AgentHttpException ?? ClassifyBuildFailure(ex, paths,
                        () => _probe.GetAvailableFreeBytes(ExistingAncestor(p.Final)), total, _minFreeBytes);
                DeleteQuietly(p.PendingPart);
                DeleteQuietly(p.PendingZip);
                DeleteQuietly(p.PendingSha);
                DeleteEmptyCreatedDirs(created);
                if (canceled) throw;
                _log.LogError(ex, "No se pudo armar o verificar el ZIP del caso {CaseId} ({Code})", p.CaseId, failure!.Code);
                if (ex is EvidenceZipVerificationException { Entry: { } d })
                    _log.LogError(
                        "Verificación del ZIP del caso {CaseId} falló: {Reason}. Entrada {Entry}, {Size} bytes, método {Method}, cifrada {Encrypted}, SHA-256 esperado {Expected}, obtenido {Actual}",
                        p.CaseId, ex.Message, d.Name, d.Size, d.Method, d.Encrypted, d.ExpectedSha256, d.ActualSha256);
                throw failure;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// <c>zip_folder</c> válido (§5.13): null, o <c>"sesion"</c> con un nombre de tramo
    /// (<see cref="SessionNames.IsSegmentName(string?)"/>).
    /// </summary>
    internal static bool IsValidZipFolder(ZipFileItem f) =>
        f.ZipFolder is null || (f.ZipFolder == SessionNames.ZipFolder && SessionNames.IsSegmentName(f.Filename));

    /// <summary>Nombre de la entrada en el ZIP: plano, o <c>sesion/&lt;filename&gt;</c>.</summary>
    internal static string EntryName(ZipFileItem f) =>
        f.ZipFolder is null ? f.Filename! : f.ZipFolder + "/" + f.Filename;

    /// <summary>
    /// Falla de los pasos 5-7 → error HTTP (tatana-ws-y-hosts T-07, D-A7): un archivo de la evidencia que
    /// desapareció a mitad del armado → 409 <c>evidence_changed</c> (<c>reason: "missing"</c>); disco lleno → 507
    /// <c>insufficient_storage</c>; cualquier otra → 500 <c>zip_failed</c>. Se llama ANTES de borrar el <c>.part</c>.
    /// </summary>
    internal static AgentHttpException ClassifyBuildFailure(Exception ex, IReadOnlyList<string> paths,
        Func<long?> freeBytes, long total, long minFree)
    {
        if (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            var missing = paths.Where(path => !File.Exists(path))
                .Select(path => new EvidenceChange(Path.GetFileName(path), "missing"))
                .ToList();
            if (missing.Count > 0)
                return new AgentHttpException(409, AgentErrorCodes.EvidenceChanged,
                    "Algunos archivos de la evidencia cambiaron o faltan en esta PC",
                    new Dictionary<string, object?> { ["files"] = missing }, ex);
        }
        if (ex is IOException io)
        {
            var available = freeBytes();
            if (DiskErrors.IsDiskFull(io) || (available is { } a && a < total))
            {
                var required = total + minFree;
                return new AgentHttpException(507, AgentErrorCodes.InsufficientStorage,
                    available is { } av
                        ? $"No hay espacio en el disco de esta PC para guardar el ZIP (hace falta {CaseEvidenceStore.FormatBytes(required)}, quedan {CaseEvidenceStore.FormatBytes(av)})"
                        : "No hay espacio en el disco de esta PC para guardar el ZIP",
                    new Dictionary<string, object?> { ["required_bytes"] = required, ["available_bytes"] = available }, ex);
            }
        }
        return new AgentHttpException(500, AgentErrorCodes.ZipFailed, ZipFailedMessage(ex), inner: ex);
    }

    /// <summary>
    /// Primer ancestro existente de <paramref name="path"/> (T-06): <c>DriveInfo</c> sobre una ruta inexistente
    /// tira en macOS y el prechequeo de espacio quedaría muerto. Último recurso: la raíz.
    /// </summary>
    internal static string ExistingAncestor(string path)
    {
        var full = Path.GetFullPath(path);
        for (var dir = full; !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
            if (Directory.Exists(dir)) return dir;
        return Path.GetPathRoot(full) ?? full;
    }

    // T-06: de la más profunda a la más alta, no recursivo y solo si quedó vacía.
    private void DeleteEmptyCreatedDirs(List<string> created)
    {
        for (var i = created.Count - 1; i >= 0; i--)
        {
            var dir = created[i];
            try
            {
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "No se pudo borrar la carpeta vacía {Dir}", dir);
            }
        }
    }

    // ── Limpieza al arrancar (T-05) ──────────────────────────────────────────

    public int CleanupOrphanParts()
    {
        if (!Directory.Exists(EvidenceDirectory)) return 0;
        var deleted = 0;
        IEnumerable<string> cases;
        try { cases = Directory.EnumerateDirectories(EvidenceDirectory).ToList(); }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se pudo recorrer {Dir} para borrar ZIP parciales", EvidenceDirectory);
            return 0;
        }
        foreach (var caseDir in cases)
        {
            var pending = Path.Combine(caseDir, MetaDirName, PendingDirName);
            try
            {
                if (!Directory.Exists(pending)) continue;
                foreach (var file in Directory.EnumerateFiles(pending).ToList())
                {
                    if (!AgentFileNames.IsZipPartFilename(Path.GetFileName(file))) continue;
                    try
                    {
                        File.Delete(file);
                        deleted++;
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "No se pudo borrar el ZIP parcial huérfano {Path}", file);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "No se pudo revisar {Dir}", pending);
            }
        }
        return deleted;
    }

    /// <summary>
    /// Texto de <c>zip_failed</c> (zip-verificacion-stored D4). Si falló la verificación del ZIP, la
    /// evidencia ya pasó el rehash (si no, habría sido <c>evidence_changed</c>), así que está intacta; el
    /// detalle técnico va solo al log.
    /// </summary>
    internal static string ZipFailedMessage(Exception ex) => ex is EvidenceZipVerificationException
        ? "La evidencia está intacta, pero el ZIP armado no pasó el control de integridad. Si se repite, contactá a soporte."
        : "No se pudo armar o verificar el ZIP en esta PC; la evidencia quedó intacta";

    // ── PUT /cases/{id}/zip/seals (sellado-tiempo §7.3) ─────────────────────

    public async Task<WriteSealsResponse> WriteSealsAsync(string caseId, WriteSealsRequest request,
        CancellationToken ct)
    {
        // 1-2. Id del caso y nombre del ZIP (PathsFor tira 400 invalid_case_id / invalid_filename).
        var p = PathsFor(caseId, request.CaseRef, request.ZipFilename);
        // 3. El ZIP final tiene que estar en esta PC.
        if (!File.Exists(p.ZipPath))
            throw new AgentHttpException(404, AgentErrorCodes.ZipNotFound, "El ZIP no está en esta PC");

        // 4. Sellos: 0..1000, nombre seguro, 1 B–64 KiB, sin repetidos. Todo se valida antes de escribir nada.
        var items = request.Files ?? [];
        if (items.Count > MaxSealFiles)
            throw InvalidSeals($"Demasiados sellos (máximo {MaxSealFiles})", null);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var decoded = new List<(string Name, byte[] Data)>(items.Count);
        foreach (var item in items)
        {
            if (!AgentFileNames.IsSealFilename(item.Name))
                throw InvalidSeals("Nombre de sello inválido", item.Name);
            if (!names.Add(item.Name!))
                throw InvalidSeals("Nombre de sello repetido", item.Name);
            byte[] data;
            try { data = Convert.FromBase64String(item.ContentBase64 ?? ""); }
            catch (FormatException) { throw InvalidSeals("El contenido del sello no es base64 válido", item.Name); }
            if (data.Length is 0 or > MaxSealBytes)
                throw InvalidSeals("El sello tiene que pesar entre 1 B y 64 KiB", item.Name);
            decoded.Add((item.Name!, data));
        }
        // 5. LEEME: ≤ 256 KiB en UTF-8, tal cual llega (sin BOM).
        byte[]? readme = null;
        if (request.Readme is { } text)
        {
            readme = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
            if (readme.Length > MaxReadmeBytes)
                throw InvalidSeals("El LEEME supera 256 KiB", SealsReadmeName);
        }

        var gate = LockFor(p.CaseId);
        if (!await gate.WaitAsync(0, ct))
            throw new AgentHttpException(409, AgentErrorCodes.ZipInProgress, "Ya se está armando el ZIP de este caso en esta PC");
        try
        {
            var dir = Path.Combine(p.Final, SealsDirName);
            Directory.CreateDirectory(dir);
            int written = 0, unchanged = 0;
            if (readme is not null) decoded.Add((SealsReadmeName, readme));
            foreach (var (name, data) in decoded)
            {
                ct.ThrowIfCancellationRequested();
                var target = AgentFileNames.SafeChild(dir, name)
                    ?? throw InvalidSeals("Nombre de sello inválido", name);
                if (File.Exists(target) && SameBytes(target, data))
                {
                    unchanged++;
                    continue;
                }
                var part = target + ".part";
                await File.WriteAllBytesAsync(part, data, ct);
                File.Move(part, target, overwrite: true);
                written++;
            }
            _log.LogInformation("Sellos del caso {CaseId} en {Dir}: {Written} escritos, {Unchanged} sin cambios",
                p.CaseId, dir, written, unchanged);
            return new WriteSealsResponse(dir, written, unchanged);
        }
        catch (AgentHttpException) { throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AgentHttpException(500, AgentErrorCodes.StorageError,
                "No se pudieron guardar los sellos en la carpeta del caso", inner: ex);
        }
        finally
        {
            gate.Release();
        }
    }

    private static AgentHttpException InvalidSeals(string message, string? filename) =>
        new(400, AgentErrorCodes.InvalidRequest, message,
            filename is null ? null : new Dictionary<string, object?> { ["filename"] = filename });

    private static bool SameBytes(string path, byte[] data)
    {
        var info = new FileInfo(path);
        if (info.Length != data.Length) return false;
        return File.ReadAllBytes(path).AsSpan().SequenceEqual(data);
    }

    // ── POST /cases/{id}/zip/commit ──────────────────────────────────────────

    public async Task<CommitZipResponse> CommitAsync(string caseId, CommitZipRequest request, CancellationToken ct)
    {
        var p = PathsFor(caseId, request.CaseRef, request.ZipFilename);
        var zipHash = request.ZipHash?.Trim().ToLowerInvariant() ?? "";

        var gate = LockFor(p.CaseId);
        if (!await gate.WaitAsync(0, ct))
            throw new AgentHttpException(409, AgentErrorCodes.ZipInProgress, "Ya se está armando el ZIP de este caso en esta PC");
        try
        {
            // Idempotente: ya confirmado con este nombre y hash.
            if (ReadState(p) is { } state && state.ZipFilename == p.ZipFilename && state.ZipHash == zipHash)
            {
                DeleteQuietly(p.PendingSha);
                var again = DeleteLoose(p.CaseId, request.DeleteFiles);
                _sessions?.TryDeleteIfClosed(p.CaseId);
                return new CommitZipResponse(p.ZipPath, again);
            }

            if (!File.Exists(p.PendingZip))
                throw new AgentHttpException(404, AgentErrorCodes.ZipNotFound, "El ZIP no está en esta PC");
            var pendingHash = File.Exists(p.PendingSha) ? (await File.ReadAllTextAsync(p.PendingSha, ct)).Trim() : null;
            if (zipHash.Length == 0 || !string.Equals(pendingHash, zipHash, StringComparison.Ordinal))
                throw new AgentHttpException(409, AgentErrorCodes.ZipHashMismatch,
                    "El ZIP de esta PC no coincide con el hash registrado; no se movió nada");

            if (File.Exists(p.ZipPath))
            {
                // Un commit anterior movió el ZIP pero no llegó a escribir estado.json.
                var existing = await _store.Sha256Async(p.ZipPath, ct);
                if (!string.Equals(existing, zipHash, StringComparison.Ordinal))
                    throw new AgentHttpException(409, AgentErrorCodes.ZipHashMismatch,
                        "Ya hay otro ZIP con ese nombre en la carpeta del caso; no se movió nada");
                DeleteQuietly(p.PendingZip);
            }
            else
            {
                File.Move(p.PendingZip, p.ZipPath, overwrite: false);
            }

            var json = JsonSerializer.Serialize(new ZipCommitState(p.ZipFilename, zipHash, DateTime.UtcNow), StateJson);
            await File.WriteAllTextAsync(p.StateFile, json, ct);
            DeleteQuietly(p.PendingSha);
            var deleted = DeleteLoose(p.CaseId, request.DeleteFiles);
            // grabacion-sesion-completa §5.10: el diario de la sesión se borra recién acá, y solo si quedó vacío.
            _sessions?.TryDeleteIfClosed(p.CaseId);
            _log.LogInformation("ZIP del caso {CaseId} confirmado en {Path} ({N} sueltos borrados)",
                p.CaseId, p.ZipPath, deleted);
            return new CommitZipResponse(p.ZipPath, deleted);
        }
        catch (AgentHttpException) { throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AgentHttpException(500, AgentErrorCodes.StorageError,
                "No se pudo mover el ZIP a su carpeta final", inner: ex);
        }
        finally
        {
            gate.Release();
        }
    }

    // D12: solo los nombres pedidos y solo dentro de cases/<id>/. Si la carpeta queda vacía, se borra.
    private int DeleteLoose(string caseId, List<string>? names)
    {
        if (names is null || names.Count == 0) return 0;
        var deleted = 0;
        foreach (var name in names.Distinct(StringComparer.Ordinal))
        {
            if (!AgentFileNames.IsValidName(name)) continue;
            try
            {
                // Los tramos de la sesión también: ya están dentro del ZIP confirmado.
                if (_store.Delete(caseId, name, allowSessionFiles: true)) deleted++;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "No se pudo borrar {Name} de la carpeta del caso {CaseId}", name, caseId);
            }
        }
        try
        {
            var dir = _store.CaseDir(caseId);
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se pudo borrar la carpeta vacía del caso {CaseId}", caseId);
        }
        return deleted;
    }

    // ── DELETE /zip/pending, GET /zip/status, ruta del final ─────────────────

    public void DiscardPending(string caseId, string? caseRef, string? zipFilename)
    {
        var p = PathsFor(caseId, caseRef, zipFilename);
        var gate = LockFor(p.CaseId);
        if (!gate.Wait(0))
            throw new AgentHttpException(409, AgentErrorCodes.ZipInProgress, "Ya se está armando el ZIP de este caso en esta PC");
        try
        {
            DeleteQuietly(p.PendingPart);
            DeleteQuietly(p.PendingZip);
            DeleteQuietly(p.PendingSha);
        }
        finally
        {
            gate.Release();
        }
    }

    public ZipStatusResponse Status(string caseId, string? caseRef, string? zipFilename)
    {
        var p = PathsFor(caseId, caseRef, zipFilename);
        if (File.Exists(p.ZipPath))
        {
            var state = ReadState(p);
            return new ZipStatusResponse("final", p.ZipPath, p.Final, null,
                state is not null && state.ZipFilename == p.ZipFilename ? state.ZipHash : null);
        }
        if (File.Exists(p.PendingZip))
        {
            string? hash = null;
            try { if (File.Exists(p.PendingSha)) hash = File.ReadAllText(p.PendingSha).Trim(); }
            catch (IOException) { }
            return new ZipStatusResponse("pending", p.ZipPath, p.Final, hash, null);
        }
        return new ZipStatusResponse("none", p.ZipPath, p.Final, null, null);
    }

    public string? FinalZipPath(string caseId, string? caseRef, string? zipFilename)
    {
        var p = PathsFor(caseId, caseRef, zipFilename);
        return File.Exists(p.ZipPath) ? p.ZipPath : null;
    }

    private ZipCommitState? ReadState(ZipPaths p)
    {
        try
        {
            if (!File.Exists(p.StateFile)) return null;
            return JsonSerializer.Deserialize<ZipCommitState>(File.ReadAllText(p.StateFile), StateJson);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "No se pudo leer {State}", p.StateFile);
            return null;
        }
    }

    private void DeleteQuietly(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { _log.LogWarning(ex, "No se pudo borrar {Path}", path); }
    }

    // ── Evento zip_progress (§6.4): throttle de 500 ms por caso y uno forzado por fase ─

    private sealed class ZipProgress(AgentWebSocketHub? hub, string caseId, long total)
    {
        private string _phase = "hashing";
        private long _done;
        private long _total = total;
        private DateTime _last = DateTime.MinValue;
        private int _sending;

        public async Task PhaseAsync(string phase)
        {
            _phase = phase;
            Interlocked.Exchange(ref _done, 0);
            await SendAsync();
        }

        public void Reset(long total) => _total = total;

        public Task DoneAsync()
        {
            Interlocked.Exchange(ref _done, _total);
            return SendAsync();
        }

        public void Add(long bytes)
        {
            Interlocked.Add(ref _done, bytes);
            var now = DateTime.UtcNow;
            if (now - _last < ProgressInterval) return;
            // Sin envíos solapados (un WebSocket admite un solo SendAsync a la vez).
            if (Interlocked.CompareExchange(ref _sending, 1, 0) != 0) return;
            _last = now;
            _ = SendCoreAsync().ContinueWith(_ => Interlocked.Exchange(ref _sending, 0), TaskScheduler.Default);
        }

        private async Task SendAsync()
        {
            _last = DateTime.UtcNow;
            // Espera a que termine un envío en curso del throttle antes del forzado.
            for (var i = 0; i < 50 && Interlocked.CompareExchange(ref _sending, 1, 0) != 0; i++)
                await Task.Delay(10);
            try { await SendCoreAsync(); }
            finally { Interlocked.Exchange(ref _sending, 0); }
        }

        private async Task SendCoreAsync()
        {
            if (hub is null) return;
            try
            {
                await hub.BroadcastAsync(new AgentEvent
                {
                    Type = "zip_progress",
                    Data = new
                    {
                        case_id = caseId,
                        phase = _phase,
                        done_bytes = Math.Min(Interlocked.Read(ref _done), _total),
                        total_bytes = _total,
                    },
                });
            }
            catch
            {
                // El progreso es informativo: nunca corta el ZIP.
            }
        }
    }
}
