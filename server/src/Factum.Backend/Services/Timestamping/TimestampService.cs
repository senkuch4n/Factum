using Factum.Backend.Infrastructure;
using Factum.Backend.Models;

namespace Factum.Backend.Services.Timestamping;

/// <summary>Despierta la cola de reintentos antes de su próxima vuelta (sellado-tiempo §6.4).</summary>
public interface ITimestampQueueSignal
{
    void Wake();
    /// <summary>Espera un <see cref="Wake"/> o el <paramref name="timeout"/>. true si lo despertaron.</summary>
    Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct);
}

public sealed class TimestampQueueSignal : ITimestampQueueSignal
{
    private readonly SemaphoreSlim _sem = new(0, 1);
    public int Wakes { get; private set; }

    public void Wake()
    {
        Wakes++;
        try { if (_sem.CurrentCount == 0) _sem.Release(); }
        catch (SemaphoreFullException) { /* ya estaba despierta */ }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _sem.WaitAsync(timeout, ct);
}

/// <summary>sellado-tiempo §6.1. Si está desactivado, todo es no-op (salvo <see cref="ListAsync"/>).</summary>
public interface ITimestampService
{
    bool IsEnabled { get; }
    /// <summary>Authority conocida (o host) de la PRIMERA URL; null si está desactivado.</summary>
    string? AuthorityHint { get; }
    /// <summary>Presupuesto de cada sello en generate/finish (<c>Timestamping:GenerationBudgetSeconds</c>).</summary>
    TimeSpan GenerationBudget { get; }

    /// <summary>
    /// Crea (idempotente) los pending de la evidencia nueva o actualizada e intenta inline con presupuesto.
    /// Nunca lanza: los errores de Mongo/TSA se loguean y el registro de evidencia responde igual.
    /// </summary>
    Task SealEvidenceAsync(Case cas, IReadOnlyList<(string Filename, string Sha256)> items, CancellationToken ct);

    /// <summary>
    /// Sello inmediato para el ZIP o el DOCX en generate/finish. Devuelve el documento (granted o pending).
    /// null si está desactivado o si falló Mongo. Nunca lanza.
    /// </summary>
    Task<CaseTimestamp?> SealNowAsync(Case cas, string target, string filename, string sha256, TimeSpan budget);

    /// <summary>
    /// extraccion-logica-android §6.3: sello inmediato del MANIFIESTO.tsv de una extracción, SIN caso
    /// (<c>CaseId == ""</c> + <c>ExtractionId</c>). No registra evento de caso. Devuelve el documento (granted o
    /// pending) o null si está desactivado o falló Mongo. Nunca lanza. Idempotente por el hash del manifiesto.
    /// </summary>
    Task<CaseTimestamp?> SealExtractionManifestAsync(Extraction ext, TimeSpan budget);

    /// <summary><c>InReport = true</c>. Nunca lanza.</summary>
    Task MarkZipInReportAsync(string timestampId);

    /// <summary>§6.3, "Error en la generación": el pending del ZIP pasa a failed. Nunca lanza.</summary>
    Task AbortZipAsync(string caseId, string zipFilename, string sha256);

    /// <summary>
    /// Sellos del caso, con el <c>.tsr</c> solo si se pide. Lee aunque el sellado esté desactivado: los sellos
    /// ya obtenidos siguen visibles y descargables (§5.6.1).
    /// </summary>
    Task<List<CaseTimestamp>> ListAsync(string caseId, CancellationToken ct, bool includeTsr = false);
}

/// <summary>
/// Servicio de sellado (sellado-tiempo §6.1, §6.5 y el disyuntor inline de §5.3). Singleton. Todos los intentos
/// —inline y de la cola— pasan por <see cref="AttemptAsync"/>, que escribe con el lease del que llama.
/// </summary>
public sealed class TimestampService : ITimestampService
{
    public const string AbortedZipMessage = "La generación no se completó; el ZIP se descartó";
    /// <summary>Margen del lease de un intento inline por sobre su presupuesto.</summary>
    private static readonly TimeSpan InlineLeaseMargin = TimeSpan.FromSeconds(30);
    /// <summary>Por debajo de esto no se arranca un intento inline (no alcanza ni para el TLS).</summary>
    private static readonly TimeSpan MinInlineBudget = TimeSpan.FromMilliseconds(500);

    private readonly TimestampingOptions.Settings _settings;
    private readonly ICaseTimestampRepository _repo;
    private readonly Func<ITimestampAuthorityClient> _client;
    private readonly ICaseEventRepository _events;
    private readonly ICaseRepository _cases;
    private readonly ITimestampQueueSignal _signal;
    private readonly TimeProvider _time;
    private readonly IHttpContextAccessor? _http;
    private readonly ILogger<TimestampService> _log;
    private readonly Random _random;

    // Disyuntor inline (D-T7): última vez que un pedido inline falló con TODAS las URLs.
    private long _lastInlineTotalFailureTicks;

    public TimestampService(TimestampingOptions.Settings settings, ICaseTimestampRepository repo,
        IServiceProvider services, ICaseEventRepository events, ICaseRepository cases, ITimestampQueueSignal signal,
        TimeProvider time, IHttpContextAccessor http, ILogger<TimestampService> log)
        // El cliente tipado (AddHttpClient) es transient: se pide uno por intento para no fijar el handler.
        : this(settings, repo, services.GetRequiredService<ITimestampAuthorityClient>, events, cases, signal, time,
            log, http, null)
    {
    }

    internal TimestampService(TimestampingOptions.Settings settings, ICaseTimestampRepository repo,
        Func<ITimestampAuthorityClient> client, ICaseEventRepository events, ICaseRepository cases,
        ITimestampQueueSignal signal, TimeProvider time, ILogger<TimestampService> log,
        IHttpContextAccessor? http = null, Random? random = null)
    {
        _settings = settings;
        _repo = repo;
        _client = client;
        _events = events;
        _cases = cases;
        _signal = signal;
        _time = time;
        _http = http;
        _log = log;
        _random = random ?? Random.Shared;
    }

    public bool IsEnabled => _settings.IsActive;

    public string? AuthorityHint
    {
        get
        {
            if (!IsEnabled) return null;
            var host = _settings.Urls[0].Host;
            return KnownAuthorities.ForHost(host)?.Name ?? host;
        }
    }

    public TimeSpan GenerationBudget => _settings.GenerationBudget;

    internal TimestampingOptions.Settings Settings => _settings;

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    // ── Evidencia (§6.2) ─────────────────────────────────────────────────────

    public async Task SealEvidenceAsync(Case cas, IReadOnlyList<(string Filename, string Sha256)> items,
        CancellationToken ct)
    {
        if (!IsEnabled || items.Count == 0) return;
        var ip = _http?.HttpContext?.Connection.RemoteIpAddress?.ToString();
        var start = Now;
        var deadline = start + _settings.InlineBudget;
        var breakerOpen = InlineBreakerOpen(start);
        if (breakerOpen)
            _log.LogInformation("Caso {CaseId}: sellado inline en pausa (la autoridad falló hace menos de {S} s); " +
                                "queda pendiente para la cola", cas.Id, _settings.InlineCooldownSeconds);
        var leftPending = false;

        foreach (var (filename, sha256) in items)
        {
            try
            {
                var now = Now;
                var remaining = deadline - now;
                var inline = !breakerOpen && remaining >= MinInlineBudget;
                var doc = NewPending(cas.Id, TimestampTargets.Evidence, filename, sha256, now,
                    inline ? remaining + InlineLeaseMargin : null);
                var inserted = await _repo.InsertPendingIfMissingAsync(doc, CancellationToken.None);
                if (inserted is null) continue; // ya existía (re-registro idéntico o carrera): no se hace nada más
                if (!inline)
                {
                    leftPending = true;
                    continue;
                }

                var result = await AttemptAsync(inserted, cas, remaining, ip, inline: true);
                if (result is { Status: TimestampStatuses.Pending }) leftPending = true;
                // Un fallo total inline abre el disyuntor: lo que sigue de este pedido va directo a la cola.
                if (result is not null && result.Status != TimestampStatuses.Granted) breakerOpen = true;
            }
            catch (Exception ex)
            {
                leftPending = true;
                _log.LogWarning(ex, "Caso {CaseId}: no se pudo registrar el sello de tiempo de {File}", cas.Id, filename);
            }
        }

        if (leftPending) _signal.Wake();
    }

    // ── Generación (§6.3) ────────────────────────────────────────────────────

    public async Task<CaseTimestamp?> SealNowAsync(Case cas, string target, string filename, string sha256,
        TimeSpan budget)
    {
        if (!IsEnabled) return null;
        try
        {
            var now = Now;
            var doc = NewPending(cas.Id, target, filename, sha256, now, budget + InlineLeaseMargin);
            var inserted = await _repo.InsertPendingIfMissingAsync(doc, CancellationToken.None);
            if (inserted is null)
                return await _repo.FindObjectAsync(cas.Id, target, filename, sha256, CancellationToken.None);

            var ip = _http?.HttpContext?.Connection.RemoteIpAddress?.ToString();
            var result = await AttemptAsync(inserted, cas, budget, ip, inline: true);
            if (result is { Status: TimestampStatuses.Pending }) _signal.Wake();
            return result ?? await _repo.FindByIdAsync(inserted.Id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Caso {CaseId}: no se pudo pedir el sello de tiempo del {Target}", cas.Id, target);
            return null;
        }
    }

    public async Task<CaseTimestamp?> SealExtractionManifestAsync(Extraction ext, TimeSpan budget)
    {
        if (!IsEnabled || string.IsNullOrEmpty(ext.ManifestSha256)) return null;
        try
        {
            var now = Now;
            var doc = NewPending("", TimestampTargets.ExtractionManifest, "MANIFIESTO.tsv", ext.ManifestSha256!, now,
                budget + InlineLeaseMargin);
            doc.ExtractionId = ext.Id;
            var inserted = await _repo.InsertPendingIfMissingAsync(doc, CancellationToken.None);
            if (inserted is null)
                return await _repo.FindByExtractionAsync(ext.Id, false, CancellationToken.None);

            var ip = _http?.HttpContext?.Connection.RemoteIpAddress?.ToString();
            // cas: null → AttemptAsync no registra ningún evento de caso (es una extracción).
            var result = await AttemptAsync(inserted, cas: null, budget, ip, inline: true);
            if (result is { Status: TimestampStatuses.Pending }) _signal.Wake();
            return result ?? await _repo.FindByIdAsync(inserted.Id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Extracción {Id}: no se pudo pedir el sello del manifiesto", ext.Id);
            return null;
        }
    }

    public async Task MarkZipInReportAsync(string timestampId)
    {
        try { await _repo.MarkInReportAsync(timestampId, CancellationToken.None); }
        catch (Exception ex) { _log.LogWarning(ex, "No se pudo marcar el sello {Id} como incluido en el informe", timestampId); }
    }

    public async Task AbortZipAsync(string caseId, string zipFilename, string sha256)
    {
        if (!IsEnabled) return;
        try { await _repo.AbortZipAsync(caseId, zipFilename, sha256, AbortedZipMessage, CancellationToken.None); }
        catch (Exception ex) { _log.LogWarning(ex, "Caso {CaseId}: no se pudo descartar el sello pendiente del ZIP", caseId); }
    }

    public Task<List<CaseTimestamp>> ListAsync(string caseId, CancellationToken ct, bool includeTsr = false) =>
        _repo.ListByCaseAsync(caseId, includeTsr, ct);

    // ── Cola (§6.4) ──────────────────────────────────────────────────────────

    /// <summary>
    /// Procesa un pendiente ya reclamado por la cola (con su lease). Devuelve el <c>kind</c> del fallo, o null si
    /// se obtuvo el sello (o si el lease se perdió).
    /// </summary>
    internal async Task<string?> ProcessClaimedAsync(CaseTimestamp claimed)
    {
        Case? cas = null;
        // extraccion-logica-android §6.3: un sello de extracción lleva CaseId == "" y no tiene caso asociado: no se
        // busca ni se registra evento de caso.
        if (string.IsNullOrEmpty(claimed.CaseId))
        {
            _log.LogInformation("Cola de sellos: extracción {Id}", claimed.ExtractionId ?? claimed.Id);
        }
        else
        {
            try { cas = await _cases.FindByIdAsync(claimed.CaseId, CancellationToken.None); }
            catch (Exception ex) { _log.LogWarning(ex, "Cola de sellos: no se pudo leer el caso {CaseId}", claimed.CaseId); }
        }
        var result = await AttemptAsync(claimed, cas, _settings.QueueBudget, ip: null, inline: false);
        return result is null || result.Status == TimestampStatuses.Granted ? null : result.LastErrorKind;
    }

    // ── Intento con lease (§6.1) ─────────────────────────────────────────────

    /// <summary>
    /// Un pedido completo y su escritura filtrando por <c>{ _id, LeaseId }</c>. Devuelve el documento como quedó,
    /// o null si el lease ya era de otro (el otro escribe su resultado).
    /// </summary>
    private async Task<CaseTimestamp?> AttemptAsync(CaseTimestamp doc, Case? cas, TimeSpan budget, string? ip,
        bool inline)
    {
        var leaseId = doc.LeaseId!;
        var attempts = doc.Attempts + 1;
        var res = await _client().RequestAsync(doc.Sha256, budget, CancellationToken.None);
        var now = Now;

        if (res.Granted)
        {
            var d = res.Data!;
            var grant = new TimestampGrant(now, res.TsaUrl!, d.Authority, d.SignerSubject, d.GenTime, d.SerialNumber,
                d.PolicyOid, res.Tsr!);
            if (!await _repo.CompleteAsync(doc.Id, leaseId, attempts, grant, CancellationToken.None))
            {
                _log.LogInformation("Sello {Id}: otro proceso tomó el lease; se descarta este resultado", doc.Id);
                return null;
            }
            Apply(doc, grant, attempts);
            _log.LogInformation("Caso {CaseId}: sello de tiempo obtenido ({Target}, {Host})", doc.CaseId, doc.Target,
                KnownAuthorities.HostOf(res.TsaUrl));
            if (cas is not null)
            {
                var evt = EventFor(cas, CaseEventTypes.TimestampGranted, doc, ip);
                evt.Detail!.Authority = d.Authority;
                evt.Detail.GenTime = d.GenTime;
                evt.Detail.SerialNumber = d.SerialNumber;
                evt.Detail.TsaUrl = res.TsaUrl;
                await RecordEventAsync(evt);
            }
            return doc;
        }

        var failure = res.Failure!;
        var final = TimestampBackoff.IsFinal(attempts, _settings.MaxAttempts);
        DateTime? next = final ? null : now + TimestampBackoff.Delay(attempts, _random);
        var reason = Rfc3161TimestampClient.Truncate(failure.Reason, 300);
        if (!await _repo.FailAsync(doc.Id, leaseId, attempts, failure.Kind, reason, next, CancellationToken.None))
        {
            _log.LogInformation("Sello {Id}: otro proceso tomó el lease; se descarta este resultado", doc.Id);
            return null;
        }
        if (inline) MarkInlineFailure(now);
        doc.Attempts = attempts;
        doc.LastError = reason;
        doc.LastErrorKind = failure.Kind;
        doc.LeaseId = null;
        doc.LeaseUntil = null;
        doc.NextAttemptAt = next;
        if (final) doc.Status = TimestampStatuses.Failed;
        _log.LogInformation("Caso {CaseId}: sello de tiempo del {Target} sin obtener (intento {N}{Final}): {Reason}",
            doc.CaseId, doc.Target, attempts, final ? ", se abandona" : "", reason);

        // D-T13: evento en el primer intento, ante una respuesta inválida y al abandonar.
        if (cas is not null && (attempts == 1 || failure.Kind == TimestampErrorKinds.InvalidResponse || final))
        {
            var evt = EventFor(cas, CaseEventTypes.TimestampFailed, doc, ip);
            evt.Detail!.Reason = reason;
            evt.Detail.Attempt = attempts;
            evt.Detail.Final = final;
            await RecordEventAsync(evt);
        }
        return doc;
    }

    private static void Apply(CaseTimestamp doc, TimestampGrant g, int attempts)
    {
        doc.Status = TimestampStatuses.Granted;
        doc.Attempts = attempts;
        doc.GrantedAt = g.GrantedAt;
        doc.TsaUrl = g.TsaUrl;
        doc.Authority = g.Authority;
        doc.SignerSubject = g.SignerSubject;
        doc.GenTime = g.GenTime;
        doc.SerialNumber = g.SerialNumber;
        doc.PolicyOid = g.PolicyOid;
        doc.NextAttemptAt = null;
        doc.LeaseId = null;
        doc.LeaseUntil = null;
        doc.LastError = null;
        doc.LastErrorKind = null;
    }

    private static CaseTimestamp NewPending(string caseId, string target, string filename, string sha256,
        DateTime now, TimeSpan? lease) => new()
    {
        CaseId = caseId,
        Target = target,
        Filename = filename,
        Sha256 = sha256.ToLowerInvariant(),
        Status = TimestampStatuses.Pending,
        Attempts = 0,
        CreatedAt = now,
        NextAttemptAt = now,
        LeaseUntil = lease is { } l ? now + l : null,
        LeaseId = lease is null ? null : Guid.NewGuid().ToString("N"),
    };

    // ── Disyuntor inline (D-T7) ──────────────────────────────────────────────

    private bool InlineBreakerOpen(DateTime now)
    {
        if (_settings.InlineCooldownSeconds <= 0) return false;
        var last = Interlocked.Read(ref _lastInlineTotalFailureTicks);
        return last > 0 && now - new DateTime(last, DateTimeKind.Utc) < _settings.InlineCooldown;
    }

    private void MarkInlineFailure(DateTime now) => Interlocked.Exchange(ref _lastInlineTotalFailureTicks, now.Ticks);

    // ── Eventos (§6.5) ───────────────────────────────────────────────────────

    // Actor: el dueño del caso (como CaseEventFor). Ip: la del request inline; null en la cola.
    private CaseEvent EventFor(Case cas, string type, CaseTimestamp doc, string? ip) => new()
    {
        CaseId = cas.Id,
        Type = type,
        ActorDni = cas.Officer.Dni,
        ActorName = cas.Officer.Name,
        Timestamp = Now,
        Ip = ip,
        Filename = doc.Filename,
        Detail = new CaseEventDetail { Target = doc.Target, Sha256 = doc.Sha256 },
    };

    private async Task RecordEventAsync(CaseEvent evt)
    {
        try { await _events.InsertAsync(evt, CancellationToken.None); }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se pudo registrar el evento {Type} del caso {CaseId}", evt.Type, evt.CaseId);
        }
    }
}
