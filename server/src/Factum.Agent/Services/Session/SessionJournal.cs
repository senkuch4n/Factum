using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Factum.Agent.Common;
using Factum.Agent.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Services.Session;

/// <summary>Resultado de leer un diario: el documento, o ilegible (JSON roto), o inexistente.</summary>
public sealed record JournalLoad(SessionJournalDoc? Doc, bool Unreadable, string? Error)
{
    public static readonly JournalLoad Missing = new(null, false, null);
    public bool Exists => Doc is not null || Unreadable;
}

/// <summary>
/// Diario de la sesión de cada caso (grabacion-sesion-completa §5.2, §5.10): <c>&lt;DataDirectory&gt;/sessions/&lt;caseId&gt;/
/// sesion.json</c>. Escritura atómica (<c>.tmp</c> + <c>Flush(true)</c> + <c>File.Move(overwrite)</c>) con un lock por caso:
/// un corte en cualquier momento deja el diario anterior o el nuevo, nunca uno a medias. Un <c>.tmp</c> huérfano se ignora.
/// </summary>
public sealed class SessionJournal
{
    private readonly string _root;
    private readonly string _sessionsRoot;
    private readonly ILogger _log;
    private readonly ConcurrentDictionary<string, object> _locks = new(StringComparer.Ordinal);

    public SessionJournal(IOptions<AgentOptions> opts, ILogger<SessionJournal> log) : this(opts.Value.DataDirectory, log)
    {
    }

    internal SessionJournal(string dataDirectory, ILogger? log = null)
    {
        _root = Path.GetFullPath(dataDirectory);
        _sessionsRoot = Path.Combine(_root, SessionNames.SessionsDirName);
        _log = log ?? NullLogger.Instance;
    }

    public string DataDirectory => _root;
    public string SessionsRoot => _sessionsRoot;

    /// <summary>Carpeta de staging del caso (no la crea). Lanza 400 <c>invalid_case_id</c> si el id no es válido.</summary>
    public string DirFor(string caseId) => Path.Combine(_sessionsRoot, CaseEvidenceStore.RequireCaseId(caseId));

    public string PathFor(string caseId) => Path.Combine(DirFor(caseId), SessionNames.JournalFileName);

    /// <summary>Ruta de staging de un tramo (<c>sessions/&lt;caseId&gt;/&lt;nombre&gt;</c>).</summary>
    public string StagingPath(string caseId, string filename) =>
        AgentFileNames.SafeChild(DirFor(caseId), filename)
        ?? throw new ArgumentException("Nombre de tramo inválido", nameof(filename));

    public object LockFor(string caseId) => _locks.GetOrAdd(CaseEvidenceStore.RequireCaseId(caseId), _ => new object());

    public bool Exists(string caseId) => File.Exists(PathFor(caseId));

    /// <summary>Lee el diario. Inexistente → <see cref="JournalLoad.Missing"/>; JSON roto → <c>Unreadable</c>.</summary>
    public JournalLoad Load(string caseId)
    {
        var path = PathFor(caseId);
        lock (LockFor(caseId))
        {
            if (!File.Exists(path)) return JournalLoad.Missing;
            try
            {
                var doc = JsonSerializer.Deserialize<SessionJournalDoc>(File.ReadAllText(path), SessionJson.File);
                if (doc is null || string.IsNullOrEmpty(doc.SessionId))
                    return new JournalLoad(null, true, "diario vacío o sin session_id");
                doc.Segments ??= [];
                doc.Gaps ??= [];
                doc.Marks ??= [];
                return new JournalLoad(doc, false, null);
            }
            catch (JsonException ex)
            {
                return new JournalLoad(null, true, ex.Message);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // No se pudo leer (bloqueado, permisos): no es "ilegible", se reintenta después.
                _log.LogWarning(ex, "No se pudo leer el diario de la sesión del caso {CaseId}", caseId);
                throw;
            }
        }
    }

    /// <summary>Escritura atómica del diario completo (fija <c>updated_at</c>).</summary>
    public void Save(SessionJournalDoc doc, DateTime nowUtc)
    {
        var dir = DirFor(doc.CaseId);
        lock (LockFor(doc.CaseId))
        {
            Directory.CreateDirectory(dir);
            doc.UpdatedAt = nowUtc;
            var final = Path.Combine(dir, SessionNames.JournalFileName);
            var tmp = Path.Combine(dir, SessionNames.JournalTmpFileName);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(doc, SessionJson.File);
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes);
                fs.Flush(flushToDisk: true);
            }
            File.Move(tmp, final, overwrite: true);
        }
    }

    /// <summary>Casos con carpeta en <c>sessions/</c> que tienen <c>sesion.json</c> (ids canónicos).</summary>
    public IReadOnlyList<string> ListCaseIds()
    {
        if (!Directory.Exists(_sessionsRoot)) return [];
        var list = new List<string>();
        foreach (var dir in Directory.EnumerateDirectories(_sessionsRoot))
        {
            var name = Path.GetFileName(dir);
            if (!AgentFileNames.TryParseCaseId(name, out var canonical) || canonical != name) continue;
            if (File.Exists(Path.Combine(dir, SessionNames.JournalFileName))) list.Add(canonical);
        }
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    /// <summary>
    /// JSON ilegible (§5.10 paso 1): <c>sesion.json</c> → <c>sesion.json.ilegible-&lt;yyyyMMdd_HHmmss&gt;</c>. No toca
    /// ningún MKV. Devuelve la ruta nueva.
    /// </summary>
    public string? QuarantineUnreadable(string caseId, DateTime nowLocal)
    {
        var path = PathFor(caseId);
        lock (LockFor(caseId))
        {
            if (!File.Exists(path)) return null;
            var dest = path + SessionNames.UnreadableSuffix +
                       nowLocal.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            dest = AdbService.UniqueDiscardPath(Path.GetDirectoryName(dest)!, Path.GetFileName(dest), File.Exists);
            File.Move(path, dest, overwrite: false);
            return dest;
        }
    }

    /// <summary>
    /// Nombres de los tramos del diario del caso (cualquier estado). Vacío si no hay diario o es ilegible. Lo usa
    /// <c>CaseEvidenceStore.Delete</c> (§5.12 <c>session_file_protected</c>).
    /// </summary>
    public IReadOnlySet<string> SegmentFilenames(string caseId)
    {
        try
        {
            var load = Load(caseId);
            return load.Doc is { } doc
                ? doc.Segments.Select(s => s.Filename).ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Borrado del diario al confirmar el ZIP (§5.10): solo si ningún tramo está abierto, no hay restauración pendiente y
    /// la carpeta tiene únicamente <c>sesion.json</c> y <c>sesion.json.tmp</c>. Si queda cualquier otra cosa, no toca
    /// nada y lo loguea. Devuelve true si borró.
    /// </summary>
    public bool TryDeleteIfClosed(string caseId)
    {
        string dir;
        try { dir = DirFor(caseId); }
        catch (AgentHttpException) { return false; }
        if (!Directory.Exists(dir)) return false;
        lock (LockFor(caseId))
        {
            try
            {
                var load = Load(caseId);
                if (load.Doc is { } doc)
                {
                    if (doc.Segments.Any(s => SegmentStates.IsOpen(s.State)) || doc.StayAwake?.RestorePending == true ||
                        SessionStates.IsActive(doc.State))
                    {
                        _log.LogInformation("Diario de la sesión del caso {CaseId} conservado: tiene tramos abiertos o una restauración pendiente", caseId);
                        return false;
                    }
                }
                var entries = Directory.EnumerateFileSystemEntries(dir).Select(Path.GetFileName).ToList();
                if (entries.Any(e => e is not (SessionNames.JournalFileName or SessionNames.JournalTmpFileName)))
                {
                    _log.LogInformation("Diario de la sesión del caso {CaseId} conservado: la carpeta tiene otros archivos", caseId);
                    return false;
                }
                foreach (var e in entries) File.Delete(Path.Combine(dir, e!));
                Directory.Delete(dir);
                _log.LogInformation("Diario de la sesión del caso {CaseId} borrado tras confirmar el ZIP", caseId);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.LogWarning(ex, "No se pudo borrar el diario de la sesión del caso {CaseId}", caseId);
                return false;
            }
        }
    }
}
