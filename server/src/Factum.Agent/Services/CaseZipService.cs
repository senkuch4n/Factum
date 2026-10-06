using System.Collections.Concurrent;
using System.Text.Json;
using Factum.Agent.Common;
using Factum.Agent.Models;
using Factum.Agent.Services.Evidence;
using Factum.Agent.WebSockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Services;

// ── Contrato (zip-local-informe-servidor §6.3 y §8.2). JSON snake_case, null omitido. ──

public sealed record ZipFileItem(string? Filename = null, long? Size = null, string? Sha256 = null);

public sealed record BuildZipRequest(string? CaseRef = null, string? ZipFilename = null, string? Password = null,
    List<ZipFileItem>? Files = null);

public sealed record BuildZipResponse(string ZipFilename, string ZipHash, long ZipSize, bool Encrypted,
    string? Encryption, string Directory, string ZipPath, string Hostname, List<ZipFileItem> Files);

public sealed record CommitZipRequest(string? CaseRef = null, string? ZipFilename = null, string? ZipHash = null,
    List<string>? DeleteFiles = null);

public sealed record CommitZipResponse(string ZipPath, int Deleted);

public sealed record ZipStatusResponse(string State, string ZipPath, string Directory, string? PendingHash,
    string? CommittedHash);

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
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(500);

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
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public CaseZipService(IOptions<AgentOptions> opts, ICaseEvidenceStore store, AgentWebSocketHub hub,
        IAgentDiskProbe probe, ILogger<CaseZipService> log)
        : this(opts.Value, store, hub, probe, log)
    {
    }

    internal CaseZipService(AgentOptions opts, ICaseEvidenceStore store, AgentWebSocketHub? hub = null,
        IAgentDiskProbe? probe = null, ILogger? log = null)
    {
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

    private SemaphoreSlim LockFor(string caseId) => _locks.GetOrAdd(caseId, _ => new SemaphoreSlim(1, 1));

    // ── POST /cases/{id}/zip ─────────────────────────────────────────────────

    public async Task<BuildZipResponse> BuildAsync(string caseId, BuildZipRequest request, CancellationToken ct)
    {
        var p = PathsFor(caseId, request.CaseRef, request.ZipFilename);
        var files = request.Files ?? [];
        if (files.Count == 0)
            throw new AgentHttpException(400, AgentErrorCodes.InvalidFilename, "No hay archivos para el ZIP");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in files)
            if (!AgentFileNames.IsValidName(f.Filename) || !names.Add(f.Filename!))
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

            try
            {
                Directory.CreateDirectory(p.PendingDir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new AgentHttpException(500, AgentErrorCodes.StorageError,
                    $"No se pudo crear la carpeta de evidencia {p.Final}", inner: ex);
            }

            // 3. Espacio en el volumen de la carpeta final.
            var total = files.Sum(f => Math.Max(0, f.Size ?? 0));
            if (_probe.GetAvailableFreeBytes(p.Final) is { } available && available < total + _minFreeBytes)
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
                expected[f.Filename!] = hash;
            }
            if (changes.Count > 0)
                throw new AgentHttpException(409, AgentErrorCodes.EvidenceChanged,
                    "Algunos archivos de la evidencia cambiaron o faltan en esta PC",
                    new Dictionary<string, object?> { ["files"] = changes });

            // 5-7. ZIP provisorio, hash y verificación.
            try
            {
                DeleteQuietly(p.PendingPart);
                await progress.PhaseAsync("zipping");
                await EvidenceZip.WriteAsync(p.PendingPart, paths, request.Password, ct, progress.Add);
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
                    files.Select(f => new ZipFileItem(f.Filename, f.Size, f.Sha256)).ToList());
            }
            catch (Exception ex)
            {
                DeleteQuietly(p.PendingPart);
                DeleteQuietly(p.PendingZip);
                DeleteQuietly(p.PendingSha);
                if (ex is OperationCanceledException && ct.IsCancellationRequested) throw;
                _log.LogError(ex, "No se pudo armar o verificar el ZIP del caso {CaseId}", p.CaseId);
                throw new AgentHttpException(500, AgentErrorCodes.ZipFailed,
                    ex is EvidenceZipVerificationException
                        ? ex.Message
                        : "No se pudo armar o verificar el ZIP en esta PC; la evidencia quedó intacta",
                    inner: ex);
            }
        }
        finally
        {
            gate.Release();
        }
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
                return new CommitZipResponse(p.ZipPath, DeleteLoose(p.CaseId, request.DeleteFiles));
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
                if (_store.Delete(caseId, name)) deleted++;
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
