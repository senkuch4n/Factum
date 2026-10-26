using Factum.Backend.Infrastructure;
using Factum.Backend.Models;

namespace Factum.Backend.Tests.Extractions;

/// <summary>Repositorio de extracciones en memoria (filtra por dueño como el real).</summary>
internal sealed class FakeExtractionRepository : IExtractionRepository
{
    public Dictionary<string, Extraction> Store { get; } = new(StringComparer.Ordinal);

    public Task<Extraction?> FindByIdAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(Store.GetValueOrDefault(id));

    public Task<List<Extraction>> ListByOfficerAsync(string dni, string? caseId = null, bool unlinkedOnly = false,
        CancellationToken ct = default)
    {
        var q = Store.Values.Where(e => e.Officer.Dni == dni);
        if (caseId is not null) q = q.Where(e => e.CaseId == caseId);
        if (unlinkedOnly) q = q.Where(e => e.CaseId is null && ExtractionStatuses.IsTerminal(e.Status));
        return Task.FromResult(q.OrderByDescending(e => e.StartedAtPc).Take(200).ToList());
    }

    public Task<bool> UpsertAsync(Extraction ext, CancellationToken ct = default)
    {
        if (Store.TryGetValue(ext.Id, out var cur) && cur.Officer.Dni != ext.Officer.Dni)
            return Task.FromResult(false); // _id de otro dueño
        Store[ext.Id] = ext;
        return Task.FromResult(true);
    }

    public Task<Extraction?> SetCaseAsync(string id, string dni, string? caseId, DateTime? linkedAt,
        CancellationToken ct = default)
    {
        if (!Store.TryGetValue(id, out var e) || e.Officer.Dni != dni) return Task.FromResult<Extraction?>(null);
        e.CaseId = caseId;
        e.LinkedAt = linkedAt;
        return Task.FromResult<Extraction?>(e);
    }
}

/// <summary>Repositorio de eventos de caso en memoria (append-only).</summary>
internal sealed class FakeCaseEventRepository : ICaseEventRepository
{
    public List<CaseEvent> Events { get; } = [];
    public Task InsertAsync(CaseEvent evt, CancellationToken ct = default) { Events.Add(evt); return Task.CompletedTask; }
    public Task<List<CaseEvent>> ListByCaseAsync(string caseId, CancellationToken ct = default) =>
        Task.FromResult(Events.Where(e => e.CaseId == caseId).ToList());
}

/// <summary>Servicio de sellado falso: cuenta las llamadas a sellar el manifiesto.</summary>
internal sealed class FakeTimestampService : Factum.Backend.Services.Timestamping.ITimestampService
{
    public bool Enabled { get; init; }
    public int SealManifestCalls { get; private set; }

    public bool IsEnabled => Enabled;
    public string? AuthorityHint => null;
    public TimeSpan GenerationBudget => TimeSpan.FromSeconds(5);

    public Task SealEvidenceAsync(Case cas, IReadOnlyList<(string Filename, string Sha256)> items, CancellationToken ct) =>
        Task.CompletedTask;
    public Task<CaseTimestamp?> SealNowAsync(Case cas, string target, string filename, string sha256, TimeSpan budget) =>
        Task.FromResult<CaseTimestamp?>(null);
    public Task<CaseTimestamp?> SealExtractionManifestAsync(Extraction ext, TimeSpan budget)
    {
        SealManifestCalls++;
        return Task.FromResult<CaseTimestamp?>(null);
    }
    public Task MarkZipInReportAsync(string timestampId) => Task.CompletedTask;
    public Task AbortZipAsync(string caseId, string zipFilename, string sha256) => Task.CompletedTask;
    public Task<List<CaseTimestamp>> ListAsync(string caseId, CancellationToken ct, bool includeTsr = false) =>
        Task.FromResult(new List<CaseTimestamp>());
}

/// <summary>Repositorio de sellos falso: solo <see cref="FindByExtractionAsync"/> (lo que usa el servicio).</summary>
internal sealed class FakeCaseTimestampRepository : ICaseTimestampRepository
{
    public Dictionary<string, CaseTimestamp> ByExtraction { get; } = new(StringComparer.Ordinal);

    public Task<CaseTimestamp?> FindByExtractionAsync(string extractionId, bool includeTsr = false,
        CancellationToken ct = default) =>
        Task.FromResult(ByExtraction.GetValueOrDefault(extractionId));

    // El servicio de extracciones no usa el resto.
    public Task<CaseTimestamp?> InsertPendingIfMissingAsync(CaseTimestamp pending, CancellationToken ct = default) =>
        throw new NotSupportedException();
    public Task<CaseTimestamp?> FindObjectAsync(string caseId, string target, string filename, string sha256,
        CancellationToken ct = default) => throw new NotSupportedException();
    public Task<CaseTimestamp?> FindByIdAsync(string id, CancellationToken ct = default) =>
        throw new NotSupportedException();
    public Task<CaseTimestamp?> ClaimDueAsync(DateTime now, DateTime leaseUntil, string leaseId,
        CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> CompleteAsync(string id, string leaseId, int attempts, TimestampGrant grant,
        CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> FailAsync(string id, string leaseId, int attempts, string kind, string error,
        DateTime? nextAttemptAt, CancellationToken ct = default) => throw new NotSupportedException();
    public Task MarkInReportAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task AbortZipAsync(string caseId, string zipFilename, string sha256, string error,
        CancellationToken ct = default) => throw new NotSupportedException();
    public Task<List<CaseTimestamp>> ListByCaseAsync(string caseId, bool includeTsr = false,
        CancellationToken ct = default) => throw new NotSupportedException();
}
