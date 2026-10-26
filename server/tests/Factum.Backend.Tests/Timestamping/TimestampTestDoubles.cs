using System.Reflection;
using System.Text.Json;
using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Factum.Backend.Services.Timestamping;
using Factum.Backend.Tests.Auth;
using Microsoft.Extensions.Logging.Abstractions;

namespace Factum.Backend.Tests.Timestamping;

/// <summary>
/// <see cref="ICaseTimestampRepository"/> en memoria con la misma semántica que el de Mongo: índice único por
/// (caso, tipo, nombre, hash), reclamo con lease y escrituras filtradas por <c>{ _id, LeaseId }</c>.
/// Devuelve copias, como el driver.
/// </summary>
public sealed class InMemoryCaseTimestampRepository : ICaseTimestampRepository
{
    private readonly object _lock = new();
    private readonly List<CaseTimestamp> _docs = [];

    public IReadOnlyList<CaseTimestamp> All
    {
        get { lock (_lock) return _docs.Select(Clone).ToList(); }
    }

    public void Seed(CaseTimestamp doc)
    {
        lock (_lock) _docs.Add(Clone(doc));
    }

    public Task<CaseTimestamp?> InsertPendingIfMissingAsync(CaseTimestamp pending, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_docs.Any(d => SameObject(d, pending.CaseId, pending.Target, pending.Filename, pending.Sha256)))
                return Task.FromResult<CaseTimestamp?>(null);
            _docs.Add(Clone(pending));
            return Task.FromResult<CaseTimestamp?>(pending);
        }
    }

    public Task<CaseTimestamp?> FindObjectAsync(string caseId, string target, string filename, string sha256,
        CancellationToken ct = default)
    {
        lock (_lock)
            return Task.FromResult(_docs.Where(d => SameObject(d, caseId, target, filename, sha256))
                .Select(WithoutTsr).FirstOrDefault());
    }

    public Task<CaseTimestamp?> FindByIdAsync(string id, CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult(_docs.Where(d => d.Id == id).Select(WithoutTsr).FirstOrDefault());
    }

    public Task<CaseTimestamp?> ClaimDueAsync(DateTime now, DateTime leaseUntil, string leaseId,
        CancellationToken ct = default)
    {
        lock (_lock)
        {
            var doc = _docs
                .Where(d => d.Status == TimestampStatuses.Pending && d.NextAttemptAt <= now &&
                            (d.LeaseUntil is null || d.LeaseUntil < now))
                .OrderBy(d => d.NextAttemptAt)
                .FirstOrDefault();
            if (doc is null) return Task.FromResult<CaseTimestamp?>(null);
            doc.LeaseUntil = leaseUntil;
            doc.LeaseId = leaseId;
            return Task.FromResult<CaseTimestamp?>(WithoutTsr(doc));
        }
    }

    public Task<bool> CompleteAsync(string id, string leaseId, int attempts, TimestampGrant grant,
        CancellationToken ct = default)
    {
        lock (_lock)
        {
            var doc = _docs.FirstOrDefault(d => d.Id == id && d.LeaseId == leaseId);
            if (doc is null) return Task.FromResult(false);
            doc.Status = TimestampStatuses.Granted;
            doc.Attempts = attempts;
            doc.GrantedAt = grant.GrantedAt;
            doc.TsaUrl = grant.TsaUrl;
            doc.Authority = grant.Authority;
            doc.SignerSubject = grant.SignerSubject;
            doc.GenTime = grant.GenTime;
            doc.SerialNumber = grant.SerialNumber;
            doc.PolicyOid = grant.PolicyOid;
            doc.Tsr = grant.Tsr;
            doc.NextAttemptAt = null;
            doc.LeaseUntil = null;
            doc.LeaseId = null;
            doc.LastError = null;
            doc.LastErrorKind = null;
            return Task.FromResult(true);
        }
    }

    public Task<bool> FailAsync(string id, string leaseId, int attempts, string kind, string error,
        DateTime? nextAttemptAt, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var doc = _docs.FirstOrDefault(d => d.Id == id && d.LeaseId == leaseId);
            if (doc is null) return Task.FromResult(false);
            doc.Attempts = attempts;
            doc.LastError = error;
            doc.LastErrorKind = kind;
            doc.LeaseUntil = null;
            doc.LeaseId = null;
            if (nextAttemptAt is { } next) doc.NextAttemptAt = next;
            else
            {
                doc.Status = TimestampStatuses.Failed;
                doc.NextAttemptAt = null;
            }
            return Task.FromResult(true);
        }
    }

    public Task MarkInReportAsync(string id, CancellationToken ct = default)
    {
        lock (_lock)
            foreach (var d in _docs.Where(d => d.Id == id)) d.InReport = true;
        return Task.CompletedTask;
    }

    public Task AbortZipAsync(string caseId, string zipFilename, string sha256, string error,
        CancellationToken ct = default)
    {
        lock (_lock)
            foreach (var d in _docs.Where(d => SameObject(d, caseId, TimestampTargets.Zip, zipFilename, sha256) &&
                                               d.Status == TimestampStatuses.Pending))
            {
                d.Status = TimestampStatuses.Failed;
                d.LastError = error;
                d.NextAttemptAt = null;
                d.LeaseId = null;
                d.LeaseUntil = null;
            }
        return Task.CompletedTask;
    }

    public Task<List<CaseTimestamp>> ListByCaseAsync(string caseId, bool includeTsr = false,
        CancellationToken ct = default)
    {
        lock (_lock)
            return Task.FromResult(_docs.Where(d => d.CaseId == caseId).OrderBy(d => d.CreatedAt)
                .Select(d => includeTsr ? Clone(d) : WithoutTsr(d)).ToList());
    }

    public Task<CaseTimestamp?> FindByExtractionAsync(string extractionId, bool includeTsr = false,
        CancellationToken ct = default)
    {
        lock (_lock)
        {
            var d = _docs.FirstOrDefault(x => x.ExtractionId == extractionId);
            return Task.FromResult(d is null ? null : includeTsr ? Clone(d) : WithoutTsr(d));
        }
    }

    /// <summary>Pone un lease vencido a un documento (simula un proceso que murió a mitad de un intento).</summary>
    public void ForceLease(string id, DateTime until, string leaseId)
    {
        lock (_lock)
            foreach (var d in _docs.Where(d => d.Id == id))
            {
                d.LeaseUntil = until;
                d.LeaseId = leaseId;
            }
    }

    private static bool SameObject(CaseTimestamp d, string caseId, string target, string filename, string sha256) =>
        d.CaseId == caseId && d.Target == target && d.Filename == filename && d.Sha256 == sha256;

    private static CaseTimestamp Clone(CaseTimestamp d)
    {
        var c = JsonSerializer.Deserialize<CaseTimestamp>(JsonSerializer.Serialize(d))!;
        return c;
    }

    private static CaseTimestamp WithoutTsr(CaseTimestamp d)
    {
        var c = Clone(d);
        c.Tsr = null;
        return c;
    }
}

/// <summary>
/// Fake TSA programable (sellado-tiempo §11.2): una cola de resultados (o uno por defecto), con demora opcional.
/// Cuenta las llamadas y los hashes pedidos. Nunca sale a la red.
/// </summary>
public sealed class FakeTsaClient : ITimestampAuthorityClient
{
    private readonly Queue<TimestampAttemptResult> _queue = new();
    private int _calls;
    public TimestampAttemptResult Default { get; set; } = Granted();
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;
    public int Calls => _calls;
    public List<(string Sha256, TimeSpan Budget)> Requests { get; } = [];
    /// <summary>Se ejecuta dentro de cada pedido (antes de devolver), p. ej. para simular otra réplica.</summary>
    public Func<Task>? During { get; set; }

    public FakeTsaClient Enqueue(params TimestampAttemptResult[] results)
    {
        foreach (var r in results) _queue.Enqueue(r);
        return this;
    }

    public async Task<TimestampAttemptResult> RequestAsync(string sha256, TimeSpan budget, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        lock (Requests) Requests.Add((sha256, budget));
        if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
        if (During is { } during) await during();
        lock (_queue) return _queue.Count > 0 ? _queue.Dequeue() : Default;
    }

    public static readonly DateTime GenTime = new(2026, 10, 14, 12, 46, 5, DateTimeKind.Utc);

    public static TimestampAttemptResult Granted(string url = "https://freetsa.org/tsr", string authority = "Free TSA",
        string serial = "0A1B2C") =>
        TimestampAttemptResult.Ok(url, new TimestampData(GenTime, serial, "1.2.3.4.1", "CN=Fake TSA", authority),
            [0x30, 0x03, 0x02, 0x01, 0x00]);

    public static TimestampAttemptResult Failed(string kind = TimestampErrorKinds.Unreachable,
        string reason = "freetsa.org: tiempo agotado") => TimestampAttemptResult.Fail(kind, reason);
}

/// <summary>Registra los eventos insertados (append-only, como el real).</summary>
public sealed class RecordingCaseEventRepository : ICaseEventRepository
{
    public List<CaseEvent> Events { get; } = [];

    public Task InsertAsync(CaseEvent evt, CancellationToken ct = default)
    {
        lock (Events) Events.Add(evt);
        return Task.CompletedTask;
    }

    public Task<List<CaseEvent>> ListByCaseAsync(string caseId, CancellationToken ct = default) =>
        Task.FromResult(Events.Where(e => e.CaseId == caseId).ToList());
}

/// <summary>
/// <see cref="ICaseRepository"/> que solo sabe <c>FindByIdAsync</c> (lo que usa la cola para el actor del
/// evento). Cualquier otro método tira.
/// </summary>
public class CaseLookupProxy : DispatchProxy
{
    public Dictionary<string, Case> Cases { get; } = new(StringComparer.Ordinal);

    public static (ICaseRepository Repo, CaseLookupProxy Proxy) Create(params Case[] cases)
    {
        var repo = DispatchProxy.Create<ICaseRepository, CaseLookupProxy>();
        var proxy = (CaseLookupProxy)(object)repo;
        foreach (var c in cases) proxy.Cases[c.Id] = c;
        return (repo, proxy);
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == nameof(ICaseRepository.FindByIdAsync))
            return Task.FromResult(Cases.GetValueOrDefault((string)args![0]!));
        throw new NotSupportedException($"El sellado no usa {targetMethod?.Name}");
    }
}

/// <summary>Armado del servicio con dobles: fake TSA, repo en memoria, reloj manual y eventos registrados.</summary>
public sealed class TimestampHarness
{
    public InMemoryCaseTimestampRepository Repo { get; } = new();
    public FakeTsaClient Tsa { get; } = new();
    public RecordingCaseEventRepository Events { get; } = new();
    public MutableTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
    public TimestampQueueSignal Signal { get; } = new();
    public ICaseRepository Cases { get; }
    public CaseLookupProxy CaseLookup { get; }
    public TimestampingOptions.Settings Settings { get; }
    public TimestampService Service { get; }
    public Case Case { get; }

    public TimestampHarness(bool enabled = true, int maxAttempts = 12, Random? random = null)
    {
        Case = new Case
        {
            Id = "3f2a9c1e-0000-4000-8000-0000000000aa",
            NroReferencia = "123/2026",
            Officer = new User { Dni = "11111111", Name = "Perito Ficticio" },
        };
        (Cases, CaseLookup) = CaseLookupProxy.Create(Case);
        Settings = TimestampingOptions.Settings.Disabled with
        {
            Enabled = enabled,
            Urls = enabled ? [new Uri("https://freetsa.org/tsr"), new Uri("http://timestamp.digicert.com")] : [],
            MaxAttempts = maxAttempts,
        };
        Service = new TimestampService(Settings, Repo, () => Tsa, Events, Cases, Signal, Time,
            NullLogger<TimestampService>.Instance, http: null, random: random ?? new Random(7));
    }

    public TimestampRetryService NewWorker() =>
        new(Service, Repo, Signal, Time, NullLogger<TimestampRetryService>.Instance);

    public static string Sha(char c) => new(c, 64);
}
