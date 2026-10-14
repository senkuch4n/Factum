using Factum.Backend.Models;
using Factum.Backend.Services.Timestamping;

namespace Factum.Backend.Tests.Timestamping;

/// <summary>
/// Cola de reintentos (sellado-tiempo §6.4, §6.5, §11.2) con la fake TSA y el repositorio en memoria, que
/// reproduce el reclamo con lease del de Mongo.
/// </summary>
public sealed class TimestampRetryServiceTests
{
    private static CaseTimestamp Pending(TimestampHarness h, string name, char sha, DateTime next, int attempts = 0) => new()
    {
        CaseId = h.Case.Id,
        Target = TimestampTargets.Evidence,
        Filename = name,
        Sha256 = TimestampHarness.Sha(sha),
        Status = TimestampStatuses.Pending,
        Attempts = attempts,
        CreatedAt = next,
        NextAttemptAt = next,
    };

    private static DateTime Now(TimestampHarness h) => h.Time.GetUtcNow().UtcDateTime;

    [Fact]
    public async Task PendienteVencido_SeProcesa_NoVencido_No()
    {
        var h = new TimestampHarness();
        h.Repo.Seed(Pending(h, "vencido.png", 'a', Now(h).AddSeconds(-1)));
        h.Repo.Seed(Pending(h, "futuro.png", 'b', Now(h).AddMinutes(5)));

        var processed = await h.NewWorker().RunOnceAsync(default);

        Assert.Equal(1, processed);
        Assert.Equal(1, h.Tsa.Calls);
        Assert.Equal(TimestampStatuses.Granted, h.Repo.All.Single(d => d.Filename == "vencido.png").Status);
        Assert.Equal(TimestampStatuses.Pending, h.Repo.All.Single(d => d.Filename == "futuro.png").Status);
        var evt = Assert.Single(h.Events.Events);
        Assert.Equal(CaseEventTypes.TimestampGranted, evt.Type);
        Assert.Null(evt.Ip); // la cola no tiene request
    }

    [Fact]
    public async Task DosReplicas_NoProcesanElMismoDocumento()
    {
        var h = new TimestampHarness();
        h.Repo.Seed(Pending(h, "a.png", 'a', Now(h).AddSeconds(-1)));
        var workerA = h.NewWorker();
        var workerB = h.NewWorker();
        // Mientras A está pidiendo el sello, corre B sobre el mismo repositorio.
        var bProcessed = -1;
        h.Tsa.During = async () =>
        {
            if (bProcessed == -1) bProcessed = await workerB.RunOnceAsync(default);
        };

        var aProcessed = await workerA.RunOnceAsync(default);

        Assert.Equal(1, aProcessed);
        Assert.Equal(0, bProcessed);
        Assert.Equal(1, h.Tsa.Calls);
        Assert.Equal(TimestampStatuses.Granted, Assert.Single(h.Repo.All).Status);
    }

    [Fact]
    public async Task LeaseVencido_SeRetoma()
    {
        var h = new TimestampHarness();
        var doc = Pending(h, "a.png", 'a', Now(h).AddMinutes(-5));
        h.Repo.Seed(doc);
        h.Repo.ForceLease(doc.Id, Now(h).AddSeconds(-1), "lease-de-un-proceso-que-murio");

        Assert.Equal(1, await h.NewWorker().RunOnceAsync(default));
        Assert.Equal(TimestampStatuses.Granted, Assert.Single(h.Repo.All).Status);
    }

    [Fact]
    public async Task LeaseVigente_NoSeToca()
    {
        var h = new TimestampHarness();
        var doc = Pending(h, "a.png", 'a', Now(h).AddMinutes(-5));
        h.Repo.Seed(doc);
        h.Repo.ForceLease(doc.Id, Now(h).AddSeconds(30), "lease-vigente");

        Assert.Equal(0, await h.NewWorker().RunOnceAsync(default));
        Assert.Equal(0, h.Tsa.Calls);
    }

    [Fact]
    public async Task MaxAttempts_Failed_ConEventoFinal()
    {
        var h = new TimestampHarness(maxAttempts: 12);
        h.Tsa.Default = FakeTsaClient.Failed(TimestampErrorKinds.HttpError, "freetsa.org: HTTP 503");
        h.Repo.Seed(Pending(h, "a.png", 'a', Now(h).AddSeconds(-1), attempts: 11));

        await h.NewWorker().RunOnceAsync(default);

        var doc = Assert.Single(h.Repo.All);
        Assert.Equal(TimestampStatuses.Failed, doc.Status);
        Assert.Equal(12, doc.Attempts);
        Assert.Null(doc.NextAttemptAt);
        var evt = Assert.Single(h.Events.Events);
        Assert.Equal(CaseEventTypes.TimestampFailed, evt.Type);
        Assert.Equal(12, evt.Detail!.Attempt);
        Assert.True(evt.Detail.Final);
    }

    [Fact]
    public async Task RespuestaInvalidaEnElIntento3_Evento()
    {
        var h = new TimestampHarness();
        h.Tsa.Default = FakeTsaClient.Failed(TimestampErrorKinds.InvalidResponse, "freetsa.org: firma del sello inválida");
        h.Repo.Seed(Pending(h, "a.png", 'a', Now(h).AddSeconds(-1), attempts: 2));

        await h.NewWorker().RunOnceAsync(default);

        var evt = Assert.Single(h.Events.Events);
        Assert.Equal(CaseEventTypes.TimestampFailed, evt.Type);
        Assert.Equal(3, evt.Detail!.Attempt);
        Assert.False(evt.Detail.Final);
        Assert.Equal(TimestampStatuses.Pending, Assert.Single(h.Repo.All).Status); // inválida no es failed
    }

    [Fact]
    public async Task ReintentoIntermedioPorRed_SinEvento()
    {
        var h = new TimestampHarness();
        h.Tsa.Default = FakeTsaClient.Failed();
        h.Repo.Seed(Pending(h, "a.png", 'a', Now(h).AddSeconds(-1), attempts: 3));

        await h.NewWorker().RunOnceAsync(default);

        Assert.Empty(h.Events.Events);
        var doc = Assert.Single(h.Repo.All);
        Assert.Equal(4, doc.Attempts);
        Assert.Equal(TimestampStatuses.Pending, doc.Status);
        Assert.InRange((doc.NextAttemptAt!.Value - Now(h)).TotalMinutes, 9, 11); // después del 4.º: 10 min ± 10 %
    }

    [Fact]
    public async Task TresUnreachableSeguidos_CortanLaVuelta()
    {
        var h = new TimestampHarness();
        h.Tsa.Default = FakeTsaClient.Failed();
        for (var i = 0; i < 5; i++)
            h.Repo.Seed(Pending(h, $"{i}.png", (char)('a' + i), Now(h).AddSeconds(-10 + i), attempts: 2));

        var processed = await h.NewWorker().RunOnceAsync(default);

        Assert.Equal(3, processed);
        Assert.Equal(3, h.Tsa.Calls);
        Assert.Equal(2, h.Repo.All.Count(d => d.Attempts == 2));
    }

    [Fact]
    public async Task UsaElPresupuestoDeLaCola()
    {
        var h = new TimestampHarness();
        h.Repo.Seed(Pending(h, "a.png", 'a', Now(h).AddSeconds(-1)));
        await h.NewWorker().RunOnceAsync(default);
        Assert.Equal(TimeSpan.FromSeconds(20), Assert.Single(h.Tsa.Requests).Budget); // 10 s × 2 URLs
    }
}
