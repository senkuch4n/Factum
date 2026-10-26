using Factum.Agent.Common;
using Factum.Agent.Models;
using Factum.Agent.Services.DeviceDetails;
using Factum.Agent.Services.Session;
using Factum.Agent.WebSockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Services.Extraction;

/// <summary>
/// Conduce la extracción lógica de Android en segundo plano (extraccion-logica-android §5). Una a la vez
/// (singleton, <see cref="IOperationSource"/> con kind <c>extraction_android</c>). El pedido HTTP vuelve enseguida;
/// las fases corren en un <see cref="Task"/> propio con su <see cref="CancellationTokenSource"/>. Publica progreso
/// (≤1/s) y los eventos <c>extraction_progress</c>/<c>extraction_finished</c>/<c>extraction_cleanup</c>.
/// El modo mock (§5.12) permite probar el flujo de punta a punta SIN celular.
/// </summary>
public sealed class ExtractionService : IOperationSource
{
    public const string OperationKind = "extraction_android";

    private readonly IOptions<AgentOptions> _opts;
    private readonly IAgentEventSink _hub;
    private readonly IDeviceDetailsService _details;
    private readonly StayAwakeKeeper _stayAwake;
    private readonly DestinationService _destinations;
    private readonly IFolderReveal _reveal;
    private readonly IHostEnvironment _env;
    private readonly TimeProvider _time;
    private readonly ILogger<ExtractionService> _log;
    private readonly ExtractionJournal _journal;
    private readonly IReadOnlyList<IRecordingStateSource> _recordingSources;
    private readonly IAndroidScreenProbe _screenProbe;
    private static readonly string AdbPath = ToolResolver.Find(AgentTools.Adb)?.Path ?? "adb";

    private readonly object _gate = new();
    private RunState? _current;

    public ExtractionService(IOptions<AgentOptions> opts, IAgentEventSink hub, IDeviceDetailsService details,
        StayAwakeKeeper stayAwake, DestinationService destinations, IFolderReveal reveal, IHostEnvironment env,
        TimeProvider time, IEnumerable<IRecordingStateSource> recordingSources, IAndroidScreenProbe screenProbe,
        ILogger<ExtractionService> log)
    {
        _opts = opts; _hub = hub; _details = details; _stayAwake = stayAwake; _destinations = destinations;
        _reveal = reveal; _env = env; _time = time; _log = log;
        _recordingSources = recordingSources.ToArray();
        _screenProbe = screenProbe;
        _journal = new ExtractionJournal(opts.Value.DataDirectory);
    }

    private ExtractionOptions Ext => _opts.Value.Extraction;
    private bool Mock => _opts.Value.Mock;
    private DateTime NowUtc => _time.GetUtcNow().UtcDateTime;

    public IEnumerable<AgentOperation> ActiveOperations
    {
        get
        {
            lock (_gate)
                if (_current is { } c && c.Snap.Status == ExtractionStatuses.Running)
                    return [new AgentOperation(OperationKind, new DateTimeOffset(c.Snap.StartedAt, TimeSpan.Zero))];
            return [];
        }
    }

    // ── Inicio (§8.2.4) ──────────────────────────────────────────────────────

    public ExtractionSnapshot Start(ExtractionStartRequest req)
    {
        lock (_gate)
        {
            if (_current is { } c && c.Snap.Status == ExtractionStatuses.Running)
                throw new AgentHttpException(409, AgentErrorCodes.ExtractionInProgress, "Ya hay una extracción en curso.",
                    new Dictionary<string, object?> { ["extraction_id"] = c.Snap.Id });
        }

        var serial = (req.Serial ?? "").Trim();
        if (serial.Length == 0) throw Bad("Falta el serial del dispositivo.");
        var structured = Clean(req.StructuredCategories);
        var fileCats = Clean(req.FileCategories);
        var customPaths = (req.CustomPaths ?? []).Select(DevicePaths.Normalize).Where(p => p is not null).Cast<string>().ToList();
        if (req.CustomPaths is { Count: > 0 } && customPaths.Count != req.CustomPaths.Count)
            throw Bad("Alguna carpeta elegida no es válida (fuera de /sdcard).");
        if (customPaths.Count > 200) throw Bad("Demasiadas carpetas (máximo 200).");
        if (customPaths.Count > 0 && !fileCats.Contains("custom")) fileCats.Add("custom");

        foreach (var k in structured.Concat(fileCats))
            if (!ValidKey(k)) throw Bad($"Categoría desconocida: {k}.");
        if (structured.Count == 0 && fileCats.Count == 0) throw Bad("Elegí al menos una categoría.");

        var op = req.Operator;
        if (string.IsNullOrWhiteSpace(op?.Dni) || string.IsNullOrWhiteSpace(op.Name))
            throw Bad("Faltan los datos del operador.");
        if (op.Dni!.Length > 100 || op.Name!.Length > 100) throw Bad("Datos del operador demasiado largos.");

        var needsConfirm = structured.Contains("call_log") || Ext.StayAwakePolicy == "always";
        if (needsConfirm && req.ConfirmDeviceModifications != true)
            throw new AgentHttpException(400, AgentErrorCodes.ConfirmationRequired,
                "Hay que confirmar las modificaciones al dispositivo.");

        // §5.7: 409 device_busy si el MISMO serial está grabando o tiene una sesión activa (mismo patrón que
        // AgentStateController.ActiveRecordings; una fuente rota no bloquea el inicio).
        foreach (var src in _recordingSources)
        {
            try
            {
                if (src.ActiveRecordings.Any(r => string.Equals(r.Serial, serial, StringComparison.Ordinal)))
                    throw new AgentHttpException(409, AgentErrorCodes.DeviceBusy,
                        "El dispositivo está grabando en este Tatana; detené la grabación antes de extraer.",
                        new Dictionary<string, object?> { ["serial"] = serial });
            }
            catch (AgentHttpException) { throw; }
            catch (Exception ex) { _log.LogWarning(ex, "No se pudo consultar grabaciones activas de una fuente"); }
        }

        // Dispositivo (en mock, sintético).
        var brief = ReadDeviceBrief(serial);

        // Destino.
        var allowed = _destinations.ResolveAllowed(req.DestinationPath ?? "");
        var check = _destinations.Check(allowed, requiredBytes: 0, brief.Manufacturer, brief.Model);
        if (!check.Writable)
            throw new AgentHttpException(422, AgentErrorCodes.DestinationNotWritable, "No se puede escribir en el destino.");
        var folderPath = _destinations.ResolveFinalFolder(allowed, brief.Manufacturer, brief.Model);

        // Parentesco no-symlink (§5.2.4).
        var parent = Directory.GetParent(folderPath);
        if (parent is { } p && p.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new AgentHttpException(422, AgentErrorCodes.DestinationNotWritable, "El destino es un enlace simbólico.");

        var id = Guid.NewGuid().ToString("D");
        var verify = req.VerifyOnDevice ?? Ext.VerifyOnDeviceDefault;
        // H1 (QA): se detecta el binario sha256sum REAL una sola vez. DeviceSha256Available = capacidad del equipo;
        // el binario (o null) va al diario y al ACTA. En mock, un binario simulado.
        var sha256Binary = Mock ? "toybox sha256sum" : DetectDeviceSha256(serial);
        var snap = new ExtractionSnapshot
        {
            Id = id,
            Status = ExtractionStatuses.Running,
            Phase = ExtractionPhases.Preparing,
            Serial = serial,
            Manufacturer = brief.Manufacturer,
            Model = brief.Model,
            AndroidVersion = brief.AndroidVersion,
            Operator = new ExtractionOperator { Dni = op.Dni!, Name = op.Name! },
            DestinationPath = allowed,
            FolderName = Path.GetFileName(folderPath),
            FolderPath = folderPath,
            StartedAt = NowUtc,
            UpdatedAt = NowUtc,
            VerifyOnDevice = verify,
            DeviceSha256Available = sha256Binary is not null,
            Categories = structured.Select(k => Cat(k, "structured"))
                .Concat(fileCats.Select(k => Cat(k, "files"))).ToList(),
        };

        var run = new RunState
        {
            Snap = snap,
            Doc = new ExtractionJournalDoc { Snapshot = snap, FolderPath = folderPath, WorkDir = Path.Combine(folderPath, ".factum") },
            Cts = new CancellationTokenSource(),
            FolderPath = folderPath,
            WorkDir = Path.Combine(folderPath, ".factum"),
            StructuredCats = structured,
            FileCats = fileCats,
            CustomPaths = customPaths,
            VerifyOnDevice = verify,
            ClientVersion = req.ClientVersion,
            FileSystem = check.FileSystem,
            FreeBytes = check.FreeBytes,
            DeviceSha256Binary = sha256Binary,
            Outcome = Mock ? (_opts.Value.MockExtractionOutcome ?? MockExtraction.Outcomes.Completed) : "completed",
        };

        lock (_gate) _current = run;
        run.Task = Task.Run(() => RunAsync(run), CancellationToken.None);
        return Clone(snap);
    }

    // ── Consultas ─────────────────────────────────────────────────────────────

    public ExtractionSnapshot? GetCurrent()
    {
        lock (_gate) return _current is { } c ? Clone(c.Snap) : null;
    }

    public ExtractionSnapshot? Get(string id)
    {
        lock (_gate) if (_current is { } c && c.Snap.Id == id) return Clone(c.Snap);
        var doc = _journal.LoadAll().FirstOrDefault(d => d.Snapshot.Id == id);
        return doc?.Snapshot;
    }

    public List<ExtractionListItem> List() =>
        _journal.LoadAll()
            .Select(d => d.Snapshot)
            .OrderByDescending(s => s.StartedAt)
            .Take(100)
            .Select(s => new ExtractionListItem
            {
                Id = s.Id, Status = s.Status, StartedAt = s.StartedAt, FinishedAt = s.FinishedAt,
                FolderPath = s.FolderPath, Manufacturer = s.Manufacturer, Model = s.Model,
                Registered = s.Registered, SealsWritten = s.SealsWritten,
            })
            .ToList();

    public ExtractionSnapshot Cancel(string id)
    {
        RunState run;
        lock (_gate)
        {
            if (_current is not { } c || c.Snap.Id != id)
                throw new AgentHttpException(409, AgentErrorCodes.ExtractionNotRunning, "La extracción no está corriendo.");
            if (c.Snap.Status != ExtractionStatuses.Running)
                throw new AgentHttpException(409, AgentErrorCodes.ExtractionNotRunning, "La extracción no está corriendo.");
            run = c;
        }
        run.Cancelled = true;
        run.Cts.Cancel();
        return Clone(run.Snap);
    }

    public (int Total, List<(string DevicePath, string Category, string Reason)> Items) GetErrors(string id,
        int offset, int limit)
    {
        var doc = (GetCurrentDoc(id) ?? _journal.LoadAll().FirstOrDefault(d => d.Snapshot.Id == id));
        if (doc is null) throw new AgentHttpException(404, AgentErrorCodes.ExtractionNotFound, "No existe la extracción.");
        var manifestFinal = Path.Combine(doc.FolderPath, "MANIFIESTO.tsv");
        var partial = Path.Combine(doc.WorkDir, "manifiesto.parcial.tsv");
        var path = File.Exists(manifestFinal) ? manifestFinal : partial;
        var items = new List<(string, string, string)>();
        if (File.Exists(path))
            foreach (var line in File.ReadLines(path))
            {
                var cols = line.Split('\t');
                if (cols.Length < 13 || cols[11] != ManifestStates.Error) continue;
                items.Add((ExtractionManifestWriter.Unescape(cols[1]), cols[3],
                    ExtractionManifestWriter.Unescape(cols[12])));
            }
        var page = items.Skip(Math.Max(0, offset)).Take(Math.Clamp(limit, 1, 1000)).ToList();
        return (items.Count, page);
    }

    public bool MarkRegistered(string id, string manifestSha256)
    {
        var doc = GetCurrentDoc(id) ?? _journal.LoadAll().FirstOrDefault(d => d.Snapshot.Id == id)
            ?? throw new AgentHttpException(404, AgentErrorCodes.ExtractionNotFound, "No existe la extracción.");
        if (!ExtractionStatuses.IsTerminal(doc.Snapshot.Status))
            throw new AgentHttpException(409, AgentErrorCodes.ExtractionNotFinished, "La extracción no terminó.");
        if (!string.Equals(doc.Snapshot.ManifestSha256, manifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new AgentHttpException(409, AgentErrorCodes.ManifestMismatch, "El hash no coincide con el de Tatana.");
        doc.Snapshot.Registered = true;
        _journal.SaveAsync(doc).GetAwaiter().GetResult();
        return true;
    }

    public (string Directory, int Written, int Unchanged) WriteSeals(string id, ExtractionSealsRequest request)
    {
        const int maxSeals = 1000, maxSealBytes = 64 * 1024, maxReadmeBytes = 256 * 1024;
        var doc = GetCurrentDoc(id) ?? _journal.LoadAll().FirstOrDefault(d => d.Snapshot.Id == id)
            ?? throw new AgentHttpException(404, AgentErrorCodes.ExtractionNotFound, "No existe la extracción.");

        var items = request.Files ?? [];
        if (items.Count > maxSeals) throw new AgentHttpException(400, AgentErrorCodes.InvalidRequest, "Demasiados sellos.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var decoded = new List<(string Name, byte[] Data)>();
        foreach (var it in items)
        {
            if (!AgentFileNames.IsSealFilename(it.Name)) throw new AgentHttpException(400, AgentErrorCodes.InvalidRequest, "Nombre de sello inválido.");
            if (!names.Add(it.Name!)) throw new AgentHttpException(400, AgentErrorCodes.InvalidRequest, "Nombre de sello repetido.");
            byte[] data;
            try { data = Convert.FromBase64String(it.ContentBase64 ?? ""); }
            catch (FormatException) { throw new AgentHttpException(400, AgentErrorCodes.InvalidRequest, "Sello no es base64."); }
            if (data.Length is 0 or > maxSealBytes) throw new AgentHttpException(400, AgentErrorCodes.InvalidRequest, "Sello entre 1 B y 64 KiB.");
            decoded.Add((it.Name!, data));
        }
        if (request.Readme is { } text)
        {
            var bytes = new System.Text.UTF8Encoding(false).GetBytes(text);
            if (bytes.Length > maxReadmeBytes) throw new AgentHttpException(400, AgentErrorCodes.InvalidRequest, "LEEME supera 256 KiB.");
            decoded.Add(("LEEME-verificacion.txt", bytes));
        }

        var dir = Path.Combine(doc.FolderPath, "sellos");
        Directory.CreateDirectory(dir);
        int written = 0, unchanged = 0;
        foreach (var (name, data) in decoded)
        {
            var target = AgentFileNames.SafeChild(dir, name)
                ?? throw new AgentHttpException(400, AgentErrorCodes.InvalidRequest, "Nombre de sello inválido.");
            if (File.Exists(target) && File.ReadAllBytes(target).AsSpan().SequenceEqual(data)) { unchanged++; continue; }
            var part = target + ".part";
            File.WriteAllBytes(part, data);
            File.Move(part, target, overwrite: true);
            written++;
        }
        if (written > 0) { doc.Snapshot.SealsWritten = true; _journal.SaveAsync(doc).GetAwaiter().GetResult(); }
        return (dir, written, unchanged);
    }

    // ── Preflight / estimate (§5.11, §5.4.6) ───────────────────────────────────

    public object Preflight(string serial)
    {
        if (Mock)
            return new
            {
                serial, platform = "android", sdk_int = 30, android_version = "11",
                manufacturer = "samsung", model = "SM-M127F",
                battery_level = 64, battery_charging = true, device_sha256_available = true, screen_state = "on",
                connector = ConnectorPreflight(),
                file_categories = FileCategoriesPreflight(mockAllExist: true),
            };
        // Real: getprop + batería (dumpsys battery) + estado de pantalla + sha256sum + categorías por existencia real.
        var brief = ReadDeviceBrief(serial);
        var (level, charging) = ReadBattery(serial);
        var screen = ReadScreenState(serial);
        var sdk = Shell(serial, "getprop ro.build.version.sdk").Trim();
        return new
        {
            serial, platform = "android",
            sdk_int = int.TryParse(sdk, out var si) ? si : (int?)null,
            android_version = brief.AndroidVersion,
            manufacturer = brief.Manufacturer, model = brief.Model,
            battery_level = level, battery_charging = charging,
            device_sha256_available = DeviceHasSha256(serial),
            screen_state = screen,
            connector = ConnectorPreflight(),
            file_categories = FileCategoriesPreflight(serial),
        };
    }

    public object Estimate(string serial, ExtractionEstimateRequest req)
    {
        var fileCats = Clean(req.FileCategories);
        var custom = (req.CustomPaths ?? []).Select(DevicePaths.Normalize).Where(p => p is not null).Cast<string>().ToList();
        if (custom.Count > 0 && !fileCats.Contains("custom")) fileCats.Add("custom");
        foreach (var k in fileCats) if (!ValidKey(k)) throw Bad($"Categoría desconocida: {k}.");

        const long structuredBytes = 50L * 1024 * 1024;
        var byCategory = new Dictionary<string, long>();
        var roots = new List<object>();
        long filesBytes = 0;

        // Raíces por categoría (dedup con la prioridad de §4.3, como la copia) y du -s -k real por raíz.
        var raw = new List<(string Root, string Category)>();
        foreach (var cat in fileCats)
        {
            var catRoots = cat == "custom" ? custom : Android.AndroidStorageFolders.ForCategory(cat).Select(DevicePaths.Normalize).Where(p => p is not null).Cast<string>().ToList();
            foreach (var r in catRoots) raw.Add((r, cat));
            byCategory[cat] = 0;
        }
        var order = new[] { "media", "downloads", "documents", "audio", "whatsapp_media", "all_storage", "custom" };
        var deduped = DevicePaths.Dedupe(raw, order);
        var sizes = Mock ? null : DuSizes(serial, deduped.Select(d => d.Path).ToList());
        foreach (var (root, category) in deduped)
        {
            long rootBytes = Mock ? 20L * 1024 * 1024 : (sizes!.GetValueOrDefault(root, 0));
            byCategory[category] = byCategory.GetValueOrDefault(category) + rootBytes;
            filesBytes += rootBytes;
            roots.Add(new { path = root, category, bytes = rootBytes });
        }
        return new
        {
            roots, by_category = byCategory, files_bytes = filesBytes, structured_bytes = structuredBytes,
            total_bytes = filesBytes + structuredBytes,
        };
    }

    private object ConnectorPreflight()
    {
        var lk = ConnectorLock.Load();
        if (Mock) return new { available = lk.HasPin, version_name = lk.VersionName, sha256 = lk.Sha256, reason = lk.HasPin ? null : ConnectorReasons.NotReleased };
        var (apk, sha) = LocateConnector();
        var v = lk.Verify(sha, _env.IsDevelopment(), Ext.AllowUnpinnedConnector);
        return v.Available
            ? new { available = true, version_name = lk.VersionName, sha256 = (string?)sha, reason = (string?)null }
            : new { available = false, version_name = lk.VersionName, sha256 = (string?)null, reason = v.Reason };
    }

    private object FileCategoriesPreflight(bool mockAllExist)
    {
        var keys = new[] { "media", "downloads", "documents", "audio", "whatsapp_media", "all_storage" };
        return keys.Select(k =>
        {
            var roots = Android.AndroidStorageFolders.ForCategory(k).ToList();
            return new { key = k, roots, available = mockAllExist && roots.Count > 0 };
        }).ToArray();
    }

    // H2 (QA): detecta de verdad qué carpetas existen en /sdcard (una sola pasada) y marca la categoría disponible si
    // al menos una de sus raíces existe. Antes se devolvía siempre available=false con equipo real.
    private object FileCategoriesPreflight(string serial)
    {
        var keys = new[] { "media", "downloads", "documents", "audio", "whatsapp_media", "all_storage" };
        var allRoots = keys.SelectMany(Android.AndroidStorageFolders.ForCategory).Distinct().ToList();
        var existing = ExistingRoots(serial, allRoots);
        return keys.Select(k =>
        {
            var roots = Android.AndroidStorageFolders.ForCategory(k).ToList();
            var present = roots.Where(existing.Contains).ToList();
            return new { key = k, roots = present, available = present.Count > 0 };
        }).ToArray();
    }

    private bool DeviceHasSha256(string serial) => DetectDeviceSha256(serial) is not null;

    /// <summary>
    /// H1 (QA): detecta el binario sha256sum REAL del dispositivo (su ruta, p. ej. <c>/system/bin/sha256sum</c>, o
    /// <c>toybox sha256sum</c>), o null si no hay. Lo usa el preflight y el arranque de la extracción, para que el
    /// ACTA nombre el binario realmente detectado y no uno fijo.
    /// </summary>
    private string? DetectDeviceSha256(string serial)
    {
        try
        {
            var r = ProcessRunner.RunArgumentListAsync(AdbPath,
                ["-s", serial, "shell", "-T",
                 "command -v sha256sum || (toybox sha256sum --help >/dev/null 2>&1 && echo 'toybox sha256sum')"],
                timeout: TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
            var bin = r.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            return string.IsNullOrEmpty(bin) ? null : bin;
        }
        catch { return null; }
    }

    /// <summary>Corre <c>adb -s S shell -T &lt;script&gt;</c> y devuelve stdout (vacío si falla). Para preflight/estimate.</summary>
    private string Shell(string serial, string script, int timeoutSec = 20)
    {
        try
        {
            var r = ProcessRunner.RunArgumentListAsync(AdbPath, ["-s", serial, "shell", "-T", script],
                timeout: TimeSpan.FromSeconds(timeoutSec)).GetAwaiter().GetResult();
            return r.ExitCode == 0 || r.Stdout.Length > 0 ? r.Stdout : "";
        }
        catch { return ""; }
    }

    // M2: batería del dispositivo (dumpsys battery). Parse puro en ExtractionDeviceQueries.
    private (int? Level, bool? Charging) ReadBattery(string serial) =>
        ExtractionDeviceQueries.ParseBattery(Shell(serial, "dumpsys battery"));

    // M2: estado de pantalla (informativo). on | off | locked | unknown.
    private string ReadScreenState(string serial)
    {
        try
        {
            var r = _screenProbe.GetStateAsync(serial, CancellationToken.None).GetAwaiter().GetResult();
            return r.State.ToString().ToLowerInvariant();
        }
        catch { return "unknown"; }
    }

    // H2: qué raíces de /sdcard existen (una sola pasada; cada raíz citada por el riesgo de espacios en WhatsApp).
    private HashSet<string> ExistingRoots(string serial, IReadOnlyList<string> roots)
    {
        if (roots.Count == 0) return [];
        var quoted = string.Join(" ", roots.Select(AdbShell.RemoteQuote));
        var script = $"for d in {quoted}; do [ -e \"$d\" ] && printf '%s\\n' \"$d\"; done";
        return ExtractionDeviceQueries.ParseExistingRoots(Shell(serial, script), roots);
    }

    // H3: du -s -k por raíz (KiB → bytes). Las raíces inexistentes no aparecen (du las manda a stderr).
    private Dictionary<string, long> DuSizes(string serial, IReadOnlyList<string> roots)
    {
        if (roots.Count == 0) return [];
        var quoted = string.Join(" ", roots.Select(AdbShell.RemoteQuote));
        return ExtractionDeviceQueries.ParseDuKib(Shell(serial, $"du -s -k {quoted}", 120));
    }

    public void Reveal(string id)
    {
        if (_destinations.Restricted)
            throw new AgentHttpException(409, AgentErrorCodes.RevealUnavailable, "Tatana en Docker no puede abrir la carpeta.");
        var doc = GetCurrentDoc(id) ?? _journal.LoadAll().FirstOrDefault(d => d.Snapshot.Id == id)
            ?? throw new AgentHttpException(404, AgentErrorCodes.ExtractionNotFound, "No existe la extracción.");
        _reveal.Reveal(Path.Combine(doc.FolderPath, "ACTA.txt"));
    }

    // ── Reconexión del connector pendiente (§5.5 paso 6) ───────────────────────

    public void OnDevicesPolled(IReadOnlyCollection<string> serials)
    {
        // Desconexión a mitad: si el serial de la extracción en curso desapareció, cancelar como interrupción.
        RunState? run;
        lock (_gate) run = _current;
        if (run is { } r && r.Snap.Status == ExtractionStatuses.Running && !serials.Contains(r.Snap.Serial)
            && !r.Cancelled && !r.SawDisconnect)
        {
            r.SawDisconnect = true;
            r.Interruption = "device_disconnected";
            r.Cts.Cancel();
        }

        // Connector pendiente de desinstalar de una extracción YA cerrada → desinstalar y adenda.
        foreach (var doc in _journal.LoadAll())
        {
            if (doc.ConnectorPendingUninstall is not { } pending) continue;
            if (!serials.Contains(pending.Serial)) continue;
            if (run is { } rr && rr.Snap.Id == doc.Snapshot.Id) continue; // la maneja el run en curso
            _ = CleanupConnectorAsync(doc, pending);
        }
    }

    private async Task CleanupConnectorAsync(ExtractionJournalDoc doc, ConnectorPending pending)
    {
        try
        {
            var installer = new ConnectorInstaller((a, c) => ProcessRunner.RunArgumentListAsync(AdbPath, a, c,
                timeout: TimeSpan.FromSeconds(60)));
            if (!await installer.UninstallAsync(pending.Serial, CancellationToken.None)) return;

            doc.ConnectorPendingUninstall = null;
            if (ExtractionStatuses.IsTerminal(doc.Snapshot.Status))
            {
                // Adenda fuera del manifiesto (ya cerrado) + re-registro.
                var stamp = NowUtc.ToString("yyyyMMdd-HHmmss");
                var adenda = Path.Combine(doc.FolderPath, $"ADENDA_ACTA_{stamp}.txt");
                var text = $"Adenda al acta de la extracción {doc.Snapshot.Id}\r\n" +
                           $"{NowUtc:yyyy-MM-ddTHH:mm:ss}Z UTC — se desinstaló la app auxiliar {pending.Package} " +
                           "al reconectar el dispositivo.\r\n";
                await File.WriteAllTextAsync(adenda, text, new System.Text.UTF8Encoding(true));
                var sha = await FileHashing.Sha256Async(adenda);
                await File.WriteAllTextAsync(adenda + ".sha256", $"{sha}  {Path.GetFileName(adenda)}\n");
                doc.Snapshot.DeviceModifications.Add(new ModificationSnapshot
                {
                    Kind = DeviceModificationKinds.ConnectorUninstalled, At = NowUtc, AfterClose = true,
                });
                doc.Snapshot.Registered = false; // que la web re-registre
            }
            await _journal.SaveAsync(doc);
            await _hub.BroadcastAsync(new AgentEvent
            {
                Type = "extraction_cleanup",
                Data = new { extraction_id = doc.Snapshot.Id, serial = pending.Serial, kind = "connector_uninstalled", at = NowUtc },
            });
        }
        catch (Exception ex) { _log.LogWarning(ex, "Limpieza del connector pendiente de {Id} falló", doc.Snapshot.Id); }
    }

    // ── Apagado (§5.9) ─────────────────────────────────────────────────────────

    public async Task ShutdownAsync(TimeSpan budget)
    {
        RunState? run;
        lock (_gate) run = _current;
        if (run is null || run.Snap.Status != ExtractionStatuses.Running) return;
        run.Interruption ??= "agent_stopped";
        run.Cts.Cancel();
        try { await run.Task.WaitAsync(budget); } catch { /* lo cierra ExtractionRecovery al próximo arranque */ }
    }

    // ── Orquestación ───────────────────────────────────────────────────────────

    private async Task RunAsync(RunState run)
    {
        var ct = run.Cts.Token;
        StayAwakeState? stay = null;
        ConnectorPending? connectorPending = null;
        try
        {
            PreparePhase(run);
            run.Manifest = new ExtractionManifestWriter(run.WorkDir);
            await SaveAndBroadcast(run);

            // Fase estructurada (orden §5.2: contacts, sms_mms, calendar, apps, call_log).
            SetPhase(run, ExtractionPhases.Structured);
            foreach (var key in OrderStructured(run.StructuredCats))
            {
                ct.ThrowIfCancellationRequested();
                if (key == "call_log")
                    (stay, connectorPending) = await RunCallLogAsync(run, stay, ct);
                else
                    await RunStructuredAsync(run, key, ct);
                await SaveAndBroadcast(run);
            }

            // Archivos.
            if (run.FileCats.Count > 0) await RunFilesAsync(run, ct);

            await ClosePhase(run, DetermineTerminal(run), ct);
        }
        catch (OperationCanceledException)
        {
            var status = run.Cancelled ? ExtractionStatuses.Cancelled : ExtractionStatuses.Interrupted;
            if (!run.Cancelled) run.InterruptionSet(run.Interruption ?? "internal_error");
            await ClosePhase(run, status, CancellationToken.None, connectorPending);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Extracción {Id} falló", run.Snap.Id);
            run.InterruptionSet("internal_error");
            await ClosePhase(run, ExtractionStatuses.Interrupted, CancellationToken.None, connectorPending);
        }
        finally
        {
            try { await _stayAwake.RestoreAsync(stay, connected: !run.SawDisconnect); } catch { /* best-effort */ }
            if (run.Manifest is not null) await run.Manifest.DisposeAsync();
        }
    }

    private void PreparePhase(RunState run)
    {
        Directory.CreateDirectory(run.FolderPath);
        foreach (var sub in new[] { "datos/crudo", "datos/normalizado", "archivos", "registro", ".factum" })
            Directory.CreateDirectory(Path.Combine(run.FolderPath, sub.Replace('/', Path.DirectorySeparatorChar)));
        // registro/comandos.log (§5.3.1): solo en el camino real (en mock no hay comandos adb).
        if (!Mock) run.CommandLog = new ExtractionCommandLog(run.FolderPath);
    }

    // Estructurada
    private async Task RunStructuredAsync(RunState run, string key, CancellationToken ct)
    {
        SetCategory(run, key, CategoryStatuses.Running);
        if (Mock)
        {
            var entries = await MockExtraction.WriteStructuredAsync(run.FolderPath, key, UriFor(key), 5, ct);
            await AppendAll(run, entries);
            var (status, reason) = MockStructuredOutcome(run, key);
            SetCategoryDone(run, key, status, reason, count: status == CategoryStatuses.NotExtracted ? null : 5);
        }
        else
        {
            var ex = new StructuredExtractor(AdbPath, run.Snap.Serial, run.FolderPath, run.CommandLog);
            var res = key == "apps" ? await ex.RunAppsAsync(ct) : await ex.RunCategoryAsync(key, ct);
            await AppendAll(run, res.Entries);
            SetCategoryDone(run, key, res.Status, res.ReasonCode, res.Count);
        }
    }

    // call_log con el connector
    private async Task<(StayAwakeState?, ConnectorPending?)> RunCallLogAsync(RunState run, StayAwakeState? stay,
        CancellationToken ct)
    {
        SetCategory(run, "call_log", CategoryStatuses.Running);
        if (Mock)
        {
            var entries = await MockExtraction.WriteStructuredAsync(run.FolderPath, "call_log",
                "content://app.factum.connector.calllog/calls", 3, ct);
            if (run.Outcome == MockExtraction.Outcomes.ConnectorBlocked)
            {
                SetCategoryDone(run, "call_log", CategoryStatuses.NotExtracted,
                    ConnectorReasonCodes.InstallBlocked, null, "INSTALL_FAILED_USER_RESTRICTED");
                return (stay, null);
            }
            await AppendAll(run, entries);
            AddModification(run, DeviceModificationKinds.ConnectorInstalled, "app.factum.connector 1.0.0 (mock)");
            AddModification(run, DeviceModificationKinds.ConnectorUninstalled, null);
            SetCategoryDone(run, "call_log", CategoryStatuses.Done, null, 3);
            return (stay, null);
        }

        // Real: verificar hash, (stay awake), instalar, exportar, (uninstall en finally de ClosePhase).
        var (apkPath, apkSha) = LocateConnector();
        var lk = ConnectorLock.Load();
        var verify = lk.Verify(apkSha, _env.IsDevelopment(), Ext.AllowUnpinnedConnector);
        if (!verify.Available)
        {
            SetCategoryDone(run, "call_log", CategoryStatuses.NotExtracted, ConnectorReasonCodes.Unavailable, null,
                verify.Reason);
            return (stay, null);
        }

        if (Ext.StayAwakePolicy is "connector" or "always")
        {
            stay = await _stayAwake.BeginAsync(stay, run.Snap.Serial);
            await _stayAwake.ReassertAsync(stay, run.Snap.Serial);
            if (stay is { Owned: true }) AddModification(run, DeviceModificationKinds.StayAwakeSet, stay.Original);
        }

        var installer = new ConnectorInstaller((a, c) => ProcessRunner.RunArgumentListAsync(AdbPath, a, c,
            timeout: TimeSpan.FromSeconds(120)));
        var install = await installer.InstallAsync(run.Snap.Serial, apkPath!, ct);
        if (!install.Installed)
        {
            SetCategoryDone(run, "call_log", CategoryStatuses.NotExtracted, install.ReasonCode, null, install.Reason);
            return (stay, null);
        }
        var pending = new ConnectorPending(run.Snap.Serial, ConnectorInstaller.Package);
        run.Doc.ConnectorPendingUninstall = pending;
        AddModification(run, DeviceModificationKinds.ConnectorInstalled, $"{ConnectorInstaller.Package} {lk.VersionName} sha256 {lk.Sha256}");
        await _journal.SaveAsync(run.Doc, CancellationToken.None);

        var ex = new StructuredExtractor(AdbPath, run.Snap.Serial, run.FolderPath, run.CommandLog);
        var res = await ex.RunCategoryAsync("call_log", ct);
        await AppendAll(run, res.Entries);
        SetCategoryDone(run, "call_log", res.Status, res.ReasonCode, res.Count);
        return (stay, pending);
    }

    // Archivos
    private async Task RunFilesAsync(RunState run, CancellationToken ct)
    {
        SetPhase(run, ExtractionPhases.Copying);
        var roots = ResolveFileRoots(run);
        foreach (var (root, category) in roots)
        {
            ct.ThrowIfCancellationRequested();
            SetCategory(run, category, CategoryStatuses.Running);
            if (Mock)
            {
                if (run.Outcome == MockExtraction.Outcomes.CancelledSlow)
                    await Task.Delay(TimeSpan.FromSeconds(60), ct); // para probar Cancelar
                var entries = await MockExtraction.WriteFilesAsync(run.FolderPath, category, 3, ct);
                await AppendAll(run, entries);
                foreach (var e in entries) { run.Snap.Totals.FilesDone++; run.Snap.Totals.BytesDone += e.SizeBytes; }
                run.Snap.Totals.FilesTotal = run.Snap.Totals.FilesDone;
                run.Snap.Totals.BytesTotal = run.Snap.Totals.BytesDone;
                // Mock: los archivos simulados traen coincide=si (device sha256 disponible); cuentan como verificados.
                if (run.VerifyOnDevice && run.Snap.DeviceSha256Available) run.DeviceHashedCount += entries.Count;
                var status = run.Outcome == MockExtraction.Outcomes.CompletedWithErrors
                    ? CategoryStatuses.DoneWithErrors : CategoryStatuses.Done;
                if (status == CategoryStatuses.DoneWithErrors)
                {
                    await run.Manifest!.AppendAsync(new ManifestEntry($"archivos/sdcard/{category}/roto.bin",
                        $"/sdcard/{category}/roto.bin", "adb pull", category, ManifestTypes.Evidence, 0, null, null,
                        null, ManifestMatch.NotComputed, false, ManifestStates.Error, "permiso denegado"), ct);
                    run.Snap.Totals.Errors++;
                }
                SetFileCategory(run, category, status, entries.Count, entries.Sum(e => e.SizeBytes),
                    status == CategoryStatuses.DoneWithErrors ? 1 : 0);
                if (run.Outcome == MockExtraction.Outcomes.Interrupted)
                { run.InterruptionSet("device_disconnected"); throw new OperationCanceledException(); }
            }
            else
            {
                await RunFilesRealAsync(run, roots, ct);
                return; // el camino real procesa todas las raíces en una pasada (inventario + hash + copia)
            }
            await SaveAndBroadcast(run, force: true);
        }
    }

    // Camino REAL de archivos (§5.2): inventory → (verifying_device) → copying, con totales, registro/ y coincide.
    private async Task RunFilesRealAsync(RunState run, List<(string Root, string Category)> roots, CancellationToken ct)
    {
        var cmd = run.CommandLog ??= new ExtractionCommandLog(run.FolderPath);
        var fx = new FileExtractor(AdbPath, run.Snap.Serial, run.FolderPath, cmd);

        // Inventory (§5.4.3): fija bytes_total/files_total reales (H4/M1).
        SetPhase(run, ExtractionPhases.Inventory);
        await SaveAndBroadcast(run, force: true);
        var inv = await fx.InventoryAsync(roots, ct);
        run.Snap.Totals.FilesTotal = inv.Files;
        run.Snap.Totals.BytesTotal = inv.Bytes;

        // SHA-256 en el dispositivo (§5.4.5 / D10): se lee ANTES de copiar para poblar `coincide` al asentar (H1).
        Dictionary<string, string>? deviceHashes = null;
        if (run.VerifyOnDevice && run.Snap.DeviceSha256Available)
        {
            SetPhase(run, ExtractionPhases.VerifyingDevice);
            await SaveAndBroadcast(run, force: true);
            deviceHashes = await fx.DeviceHashesAsync(roots, ct);
        }

        // Copia por raíz.
        SetPhase(run, ExtractionPhases.Copying);
        long baseline = 0;
        foreach (var (root, category) in roots)
        {
            ct.ThrowIfCancellationRequested();
            SetCategory(run, category, CategoryStatuses.Running);
            var b = baseline;
            var res = await fx.CopyRootAsync(root, category, deviceHashes,
                bytes => { run.Snap.Totals.BytesDone = b + bytes; }, ct);
            await AppendAll(run, res.Entries);
            baseline += res.Bytes;
            run.Snap.Totals.FilesDone += res.Files;
            run.Snap.Totals.BytesDone = baseline;
            run.Snap.Totals.Errors += res.Errors;
            run.Snap.Totals.HashMismatches += res.HashMismatches;
            run.DeviceHashedCount += res.DeviceHashed;
            SetFileCategory(run, category, res.Status, res.Files, res.Bytes, res.Errors);
            run.Snap.Categories.First(c => c.Key == category).HashMismatches = res.HashMismatches;
            await SaveAndBroadcast(run, force: true);
        }
    }

    private async Task ClosePhase(RunState run, string status, CancellationToken ct,
        ConnectorPending? connectorPending = null)
    {
        SetPhase(run, ExtractionPhases.Closing);

        // Desinstalar connector si quedó instalado (real).
        if (!Mock && connectorPending is { } cp)
        {
            try
            {
                var installer = new ConnectorInstaller((a, c) => ProcessRunner.RunArgumentListAsync(AdbPath, a, c,
                    timeout: TimeSpan.FromSeconds(60)));
                if (await installer.UninstallAsync(cp.Serial, CancellationToken.None))
                {
                    run.Doc.ConnectorPendingUninstall = null;
                    AddModification(run, DeviceModificationKinds.ConnectorUninstalled, null);
                }
            }
            catch { /* queda pendiente para OnDevicesPolled */ }
        }

        // ACTA + LEEME + manifiesto + CIERRE.
        var acta = BuildActaData(run, status);
        try { await ExtractionActaWriter.WriteAsync(run.FolderPath, acta, ct); } catch (Exception ex) { _log.LogWarning(ex, "ACTA"); }
        // Las entradas del acta/leeme van al manifiesto (tipo=acta).
        await AppendActaEntries(run);
        var (sha, entries) = await ExtractionManifestWriter.CloseAsync(run.WorkDir, run.FolderPath, ct);
        var actaTxtSha = await SafeSha(Path.Combine(run.FolderPath, "ACTA.txt"));
        var actaJsonSha = await SafeSha(Path.Combine(run.FolderPath, "ACTA.json"));
        var cierre = ExtractionActaWriter.BuildCierre(run.Snap.Id, StatusLabel(status), DateTime.Now,
            LocalOffset(), sha, actaTxtSha, actaJsonSha, entries);
        await File.WriteAllTextAsync(Path.Combine(run.FolderPath, "CIERRE.txt"), cierre, new System.Text.UTF8Encoding(true), ct);

        run.Snap.ManifestSha256 = sha;
        run.Snap.ManifestEntries = entries;
        run.Snap.FinishedAt = NowUtc;
        run.Snap.Phase = ExtractionPhases.Done;
        run.Snap.Status = status;
        if (status == ExtractionStatuses.Interrupted)
            run.Snap.Interruption = new InterruptionSnapshot { ReasonCode = run.Interruption ?? "internal_error", At = NowUtc };
        foreach (var c in run.Snap.Categories)
            if (c.Status is CategoryStatuses.Pending or CategoryStatuses.Running)
                c.Status = status == ExtractionStatuses.Cancelled ? CategoryStatuses.Cancelled : CategoryStatuses.NotExtracted;
        run.Snap.Record = BuildRecord(run);

        // .factum se conserva si hubo error; se borra si cerró bien.
        if (status is ExtractionStatuses.Completed && Directory.Exists(run.WorkDir))
            try { Directory.Delete(run.WorkDir, recursive: true); } catch { /* best-effort */ }

        await _journal.SaveAsync(run.Doc, CancellationToken.None);
        await _hub.BroadcastAsync(new AgentEvent { Type = "extraction_finished", Data = Clone(run.Snap) });
        lock (_gate) if (_current == run) _current = null;
    }

    // ── helpers de estado/broadcast ────────────────────────────────────────────

    private void SetPhase(RunState run, string phase) { run.Snap.Phase = phase; run.Snap.UpdatedAt = NowUtc; }
    private void SetCategory(RunState run, string key, string status)
    { var c = run.Snap.Categories.First(x => x.Key == key); c.Status = status; }

    private void SetCategoryDone(RunState run, string key, string status, string? reasonCode, long? count,
        string? reason = null)
    {
        var c = run.Snap.Categories.First(x => x.Key == key);
        c.Status = status; c.Count = count; c.ReasonCode = reasonCode; c.Reason = reason;
    }

    private void SetFileCategory(RunState run, string key, string status, int files, long bytes, int errors)
    {
        var c = run.Snap.Categories.First(x => x.Key == key);
        c.Status = status; c.FilesTotal = files; c.FilesDone = files; c.BytesTotal = bytes; c.BytesDone = bytes;
        c.Errors = errors;
    }

    private void AddModification(RunState run, string kind, string? detail)
    {
        run.Snap.DeviceModifications.Add(new ModificationSnapshot { Kind = kind, At = NowUtc, Detail = detail });
    }

    private async Task AppendAll(RunState run, List<ManifestEntry> entries)
    {
        foreach (var e in entries) await run.Manifest!.AppendAsync(e, run.Cts.Token);
    }

    private async Task AppendActaEntries(RunState run)
    {
        foreach (var name in new[] { "ACTA.txt", "ACTA.json", "LEEME.txt" })
        {
            var abs = Path.Combine(run.FolderPath, name);
            if (!File.Exists(abs)) continue;
            var sha = await FileHashing.Sha256Async(abs);
            await run.Manifest!.AppendAsync(new ManifestEntry(name, "", "factum", "acta", ManifestTypes.Acta,
                new FileInfo(abs).Length, null, sha, null, ManifestMatch.NotApplicable, false, ManifestStates.Ok, null));
        }
    }

    private async Task SaveAndBroadcast(RunState run, bool force = false)
    {
        run.Snap.UpdatedAt = NowUtc;
        var now = _time.GetUtcNow();
        if (!force && now - run.LastBroadcast < TimeSpan.FromMilliseconds(Math.Max(1000, Ext.ProgressIntervalMs)))
        {
            await _journal.SaveAsync(run.Doc, CancellationToken.None);
            return;
        }
        run.LastBroadcast = now;
        await _journal.SaveAsync(run.Doc, CancellationToken.None);
        await _hub.BroadcastAsync(new AgentEvent { Type = "extraction_progress", Data = Clone(run.Snap) });
    }

    private ExtractionJournalDoc? GetCurrentDoc(string id)
    { lock (_gate) return _current is { } c && c.Snap.Id == id ? c.Doc : null; }

    // ── helpers varios ─────────────────────────────────────────────────────────

    private static CategorySnapshot Cat(string key, string kind) => new() { Key = key, Kind = kind, Status = CategoryStatuses.Pending };

    private (string Manufacturer, string Model, string AndroidVersion) ReadDeviceBrief(string serial)
    {
        if (Mock) return ("samsung", "SM-M127F", "11");
        string Prop(string p)
        {
            try { return ProcessRunner.RunAsync(AdbPath, ["-s", serial, "shell", "getprop", p]).GetAwaiter().GetResult().Stdout.Trim(); }
            catch { return ""; }
        }
        var man = Prop("ro.product.manufacturer");
        var model = Prop("ro.product.model");
        var ver = Prop("ro.build.version.release");
        return (man.Length == 0 ? "dispositivo" : man, model.Length == 0 ? "android" : model, ver);
    }

    private (string? Path, string? Sha) LocateConnector()
    {
        var path = Ext.ConnectorApkPath;
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(AppContext.BaseDirectory, "tools", "connector", "factum-connector.apk");
        if (!File.Exists(path)) return (null, null);
        return (path, FileHashing.Sha256Async(path).GetAwaiter().GetResult());
    }

    private List<(string Root, string Category)> ResolveFileRoots(RunState run)
    {
        var raw = new List<(string, string)>();
        foreach (var cat in run.FileCats)
        {
            if (cat == "custom") { foreach (var p in run.CustomPaths) raw.Add((p, "custom")); continue; }
            foreach (var r in Android.AndroidStorageFolders.ForCategory(cat))
            { var n = DevicePaths.Normalize(r); if (n is not null) raw.Add((n, cat)); }
        }
        var order = new[] { "media", "downloads", "documents", "audio", "whatsapp_media", "all_storage", "custom" };
        return DevicePaths.Dedupe(raw, order);
    }

    private static IEnumerable<string> OrderStructured(List<string> cats)
    {
        foreach (var k in new[] { "contacts", "sms_mms", "calendar", "apps", "call_log" })
            if (cats.Contains(k)) yield return k;
    }

    private (string Status, string? Reason) MockStructuredOutcome(RunState run, string key) =>
        (CategoryStatuses.Done, null);

    /// <summary>Terminal exitoso: completed, o completed_with_errors si hay categorías not_extracted/con error.</summary>
    private static string DetermineTerminal(RunState run) =>
        run.Snap.Categories.Any(c => c.Status is CategoryStatuses.NotExtracted or CategoryStatuses.DoneWithErrors)
        || run.Snap.Totals.Errors > 0
            ? ExtractionStatuses.CompletedWithErrors
            : ExtractionStatuses.Completed;

    private AgentExtractionRecord BuildRecord(RunState run)
    {
        var s = run.Snap;
        return new AgentExtractionRecord(
            Status: s.Status, Hostname: Environment.MachineName, OsUser: Environment.UserName,
            AgentVersion: AgentVersion.Current, AgentMode: null,
            StartedAtPc: new DateTimeOffset(s.StartedAt, TimeSpan.Zero),
            FinishedAtPc: s.FinishedAt is { } f ? new DateTimeOffset(f, TimeSpan.Zero) : null,
            Device: new AgentExtractionDevice(s.Serial, s.Manufacturer, s.Model, "android", s.AndroidVersion),
            Categories: s.Categories.Select(c => new AgentExtractionCategory(c.Key, c.Kind, c.Status, c.Count,
                c.FilesTotal, c.BytesTotal, c.Errors, c.HashMismatches, c.ReasonCode,
                c.Kind == "files" ? "adb pull -a" : "content query / pm list")).ToList(),
            CustomPathsCount: run.CustomPaths.Count,
            VerifyOnDevice: s.VerifyOnDevice, DeviceSha256Available: s.DeviceSha256Available,
            Totals: new AgentExtractionTotals(s.Totals.FilesTotal, s.Totals.BytesTotal, s.Totals.Errors, s.Totals.HashMismatches),
            DestinationPath: s.DestinationPath, FolderName: s.FolderName,
            ManifestSha256: s.ManifestSha256, ManifestEntries: s.ManifestEntries,
            DeviceModifications: s.DeviceModifications.Select(m => new AgentDeviceModification(m.Kind,
                new DateTimeOffset(m.At, TimeSpan.Zero), m.Detail, m.AfterClose ?? false)).ToList(),
            Interruption: s.Interruption is { } i ? new AgentExtractionInterruption(i.ReasonCode, i.Message,
                new DateTimeOffset(i.At, TimeSpan.Zero)) : null);
    }

    private ActaData BuildActaData(RunState run, string status)
    {
        var s = run.Snap;
        return new ActaData(
            ExtractionId: s.Id, Status: status, StatusLabel: StatusLabel(status),
            OperatorDni: s.Operator.Dni, OperatorName: s.Operator.Name,
            Hostname: Environment.MachineName, OsUser: Environment.UserName, Os: Environment.OSVersion.VersionString,
            AgentVersion: AgentVersion.Current, AgentMode: "installed", ClientVersion: run.ClientVersion,
            Device: new ActaDevice(s.Serial, s.Manufacturer, s.Model, s.AndroidVersion),
            StartedAtPcUtc: s.StartedAt, FinishedAtPcUtc: NowUtc, PcMinusServerMs: null, PcServerMarginMs: null,
            DestinationPath: s.FolderPath, FileSystem: run.FileSystem, FreeBytesAtStart: run.FreeBytes,
            Ficha: DeviceFicha(s), ClockStartText: null, ClockCloseText: null,
            Categories: s.Categories.Select(c => new ActaCategoryLine(c.Key, c.Key, c.Kind == "files" ? "adb pull -a"
                : "content query / pm list", c.Status, c.Count, c.FilesTotal, c.BytesTotal, c.Errors, c.HashMismatches,
                c.Reason ?? c.ReasonCode)).ToList(),
            Verification: new ActaVerification(
                Requested: run.VerifyOnDevice,
                CapabilityAvailable: s.DeviceSha256Available,
                Binary: run.DeviceSha256Binary,
                FilesComputed: (int)run.DeviceHashedCount,
                Mismatches: s.Totals.HashMismatches,
                FilesNotComputed: Math.Max(0, s.Totals.FilesDone - (int)run.DeviceHashedCount)),
            Modifications: s.DeviceModifications.Select(m => new ActaModification(m.Kind, m.At, m.Detail)).ToList(),
            InterruptionReasonText: s.Interruption?.ReasonCode, InterruptionAtUtc: s.Interruption?.At,
            ManifestSha256: s.ManifestSha256 ?? "");
    }

    // Ficha mínima desde el snapshot (identificación del equipo). En el camino real, la ficha #79 completa se puede
    // sumar leyendo IDeviceDetailsService (QA con dispositivo); acá se vuelcan siempre marca/modelo/serie/versión.
    private static List<ActaFichaField> DeviceFicha(ExtractionSnapshot s) =>
    [
        new ActaFichaField("serial_number", string.IsNullOrEmpty(s.Serial) ? null : s.Serial,
            string.IsNullOrEmpty(s.Serial) ? "empty" : null),
        new ActaFichaField("manufacturer", string.IsNullOrEmpty(s.Manufacturer) ? null : s.Manufacturer,
            string.IsNullOrEmpty(s.Manufacturer) ? "empty" : null),
        new ActaFichaField("model", string.IsNullOrEmpty(s.Model) ? null : s.Model,
            string.IsNullOrEmpty(s.Model) ? "empty" : null),
        new ActaFichaField("os_version", string.IsNullOrEmpty(s.AndroidVersion) ? null : s.AndroidVersion,
            string.IsNullOrEmpty(s.AndroidVersion) ? "empty" : null),
    ];

    private static string StatusLabel(string status) => status switch
    {
        ExtractionStatuses.Completed => "Completa",
        ExtractionStatuses.CompletedWithErrors => "Completa con errores",
        ExtractionStatuses.Interrupted => "Interrumpida",
        ExtractionStatuses.Cancelled => "Cancelada",
        _ => "En curso",
    };

    private static string LocalOffset()
    {
        var off = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
        return (off < TimeSpan.Zero ? "-" : "+") + off.ToString(@"hh\:mm");
    }

    private static async Task<string> SafeSha(string path)
    { try { return await FileHashing.Sha256Async(path); } catch { return ""; } }

    private static string UriFor(string key) => key switch
    {
        "contacts" => "content://com.android.contacts/contacts",
        "sms_mms" => "content://sms",
        "calendar" => "content://com.android.calendar/events",
        "apps" => "pm list packages",
        _ => "content://" + key,
    };

    private static List<string> Clean(List<string>? xs) =>
        (xs ?? []).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToList();

    private static bool ValidKey(string k) => Enumerable.Contains(
        new[] { "contacts", "sms_mms", "call_log", "calendar", "apps", "media", "downloads", "documents", "audio",
            "whatsapp_media", "all_storage", "custom" }, k);

    private static AgentHttpException Bad(string msg) => new(400, AgentErrorCodes.InvalidRequest, msg);

    private static ExtractionSnapshot Clone(ExtractionSnapshot s)
    {
        // Copia defensiva para serializar fuera del lock (el hub serializa async).
        var json = System.Text.Json.JsonSerializer.Serialize(s, CloneOpts);
        return System.Text.Json.JsonSerializer.Deserialize<ExtractionSnapshot>(json, CloneOpts)!;
    }

    // JsonIgnore(WhenWritingNull) a nivel propiedad se respeta igual; esto es solo para clonar.
    private static readonly System.Text.Json.JsonSerializerOptions CloneOpts =
        new() { IncludeFields = false };

    private sealed class RunState
    {
        public required ExtractionSnapshot Snap;
        public required ExtractionJournalDoc Doc;
        public required CancellationTokenSource Cts;
        public Task Task = Task.CompletedTask;
        public required string FolderPath;
        public required string WorkDir;
        public ExtractionManifestWriter? Manifest;
        public ExtractionCommandLog? CommandLog;
        public DateTimeOffset LastBroadcast = DateTimeOffset.MinValue;
        public bool VerifyOnDevice;
        public List<string> StructuredCats = [];
        public List<string> FileCats = [];
        public List<string> CustomPaths = [];
        public string Outcome = "completed";
        public string? ClientVersion;
        public string FileSystem = "";
        public long FreeBytes;
        public string? DeviceSha256Binary;   // binario sha256sum detectado (o null)
        public long DeviceHashedCount;        // archivos con hash del dispositivo comparado (coincide si/no)
        public bool Cancelled;
        public bool SawDisconnect;
        public string? Interruption;

        public void InterruptionSet(string reason) => Interruption = reason;
    }
}
