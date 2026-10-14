using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Factum.Backend.Tests.Admin;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Factum.Backend.Tests.Timestamping;

/// <summary>
/// <see cref="CaseTimestampRepository"/> contra un Mongo real (sellado-tiempo §4.1, §6.1, §6.4). Se saltea sin
/// <c>FACTUM_TEST_MONGO</c>. Cada clase usa una base PROPIA (<c>factum_test_&lt;guid&gt;</c>) que crea y al final borra;
/// nunca toca la base <c>factum</c> ni la de desarrollo.
/// </summary>
public sealed class MongoCaseTimestampRepositoryTests : IAsyncLifetime
{
    private readonly string _conn = Environment.GetEnvironmentVariable(MongoFactAttribute.EnvVar) ?? "";
    private readonly string _db = "factum_test_" + Guid.NewGuid().ToString("N");
    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

    private CaseTimestampRepository Repo() => new(
        Options.Create(new MongoOptions { ConnectionString = _conn, DatabaseName = _db }),
        NullLogger<CaseTimestampRepository>.Instance);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(_conn)) return;
        Assert.StartsWith("factum_test_", _db);
        await new MongoClient(_conn).DropDatabaseAsync(_db);
    }

    private static CaseTimestamp Pending(string filename, char sha, string? leaseId = null) => new()
    {
        CaseId = "c1",
        Target = TimestampTargets.Evidence,
        Filename = filename,
        Sha256 = new string(sha, 64),
        CreatedAt = Now,
        NextAttemptAt = Now,
        LeaseId = leaseId,
        LeaseUntil = leaseId is null ? null : Now.AddSeconds(40),
    };

    [MongoFact]
    public async Task Upsert_Idempotente_Lease_Complete_YProyeccionSinTsr()
    {
        var repo = Repo();
        var first = await repo.InsertPendingIfMissingAsync(Pending("a.png", 'a', "L1"));
        Assert.NotNull(first);
        Assert.Null(await repo.InsertPendingIfMissingAsync(Pending("a.png", 'a', "L2"))); // ya existía
        Assert.NotNull(await repo.InsertPendingIfMissingAsync(Pending("a.png", 'b')));    // mismo nombre, otro hash

        // Con el lease vigente, la cola no lo toma; el de 'b' (sin lease) sí.
        var claimed = await repo.ClaimDueAsync(Now.AddSeconds(1), Now.AddSeconds(121), "Q1");
        Assert.Equal(new string('b', 64), claimed!.Sha256);
        Assert.Null(await repo.ClaimDueAsync(Now.AddSeconds(1), Now.AddSeconds(121), "Q2"));

        // Escritura con otro LeaseId: no matchea.
        var grant = new TimestampGrant(Now, "https://freetsa.org/tsr", "Free TSA", "CN=x", Now, "0A", "1.2", [1, 2, 3]);
        Assert.False(await repo.CompleteAsync(first!.Id, "otro", 1, grant));
        Assert.True(await repo.CompleteAsync(first.Id, "L1", 1, grant));

        var list = await repo.ListByCaseAsync("c1");
        Assert.Equal(2, list.Count);
        Assert.All(list, t => Assert.Null(t.Tsr));
        var granted = list.Single(t => t.Id == first.Id);
        Assert.Equal(TimestampStatuses.Granted, granted.Status);
        Assert.Null(granted.LeaseId);
        Assert.Null(granted.NextAttemptAt);
        Assert.Equal([1, 2, 3], (await repo.ListByCaseAsync("c1", includeTsr: true)).Single(t => t.Id == first.Id).Tsr);
    }

    [MongoFact]
    public async Task Fail_Final_Y_AbortZip_Y_MarkInReport()
    {
        var repo = Repo();
        var doc = (await repo.InsertPendingIfMissingAsync(Pending("a.png", 'a', "L1")))!;
        Assert.True(await repo.FailAsync(doc.Id, "L1", 1, TimestampErrorKinds.Unreachable, "x", Now.AddMinutes(1)));
        var after = await repo.FindByIdAsync(doc.Id);
        Assert.Equal((TimestampStatuses.Pending, 1, (string?)null), (after!.Status, after.Attempts, after.LeaseId));
        // Lease vencido → la cola lo reclama.
        var claimed = await repo.ClaimDueAsync(Now.AddMinutes(2), Now.AddMinutes(4), "Q1");
        Assert.Equal(doc.Id, claimed!.Id);
        Assert.True(await repo.FailAsync(doc.Id, "Q1", 12, TimestampErrorKinds.HttpError, "y", null));
        Assert.Equal(TimestampStatuses.Failed, (await repo.FindByIdAsync(doc.Id))!.Status);

        var zip = Pending("evidencia_x.zip", 'z', "L9");
        zip.Target = TimestampTargets.Zip;
        zip = (await repo.InsertPendingIfMissingAsync(zip))!;
        await repo.AbortZipAsync("c1", "evidencia_x.zip", new string('z', 64), "abortado");
        var aborted = await repo.FindObjectAsync("c1", TimestampTargets.Zip, "evidencia_x.zip", new string('z', 64));
        Assert.Equal((TimestampStatuses.Failed, "abortado"), (aborted!.Status, aborted.LastError));
        await repo.MarkInReportAsync(zip.Id);
        Assert.True((await repo.FindByIdAsync(zip.Id))!.InReport);
    }
}
