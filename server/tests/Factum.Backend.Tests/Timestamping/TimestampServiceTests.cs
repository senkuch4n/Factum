using Factum.Backend.Models;
using Factum.Backend.Services.Timestamping;

namespace Factum.Backend.Tests.Timestamping;

/// <summary>
/// <see cref="TimestampService"/> (sellado-tiempo §6.1, §6.2, §6.5, §5.3) con fake TSA, repositorio en memoria,
/// reloj manual y eventos registrados. Sin Mongo ni red.
/// </summary>
public sealed class TimestampServiceTests
{
    [Fact]
    public async Task AltaIdempotente_DosLlamadasIguales_UnDocumentoYUnaLlamadaALaTsa()
    {
        var h = new TimestampHarness();
        await h.Service.SealEvidenceAsync(h.Case, [("captura_1.png", TimestampHarness.Sha('a'))], default);
        await h.Service.SealEvidenceAsync(h.Case, [("captura_1.png", TimestampHarness.Sha('a'))], default);

        Assert.Single(h.Repo.All);
        Assert.Equal(1, h.Tsa.Calls);
    }

    [Fact]
    public async Task EvidenciaActualizada_ConservaElSelloAnterior()
    {
        var h = new TimestampHarness();
        await h.Service.SealEvidenceAsync(h.Case, [("captura_1.png", TimestampHarness.Sha('a'))], default);
        await h.Service.SealEvidenceAsync(h.Case, [("captura_1.png", TimestampHarness.Sha('b'))], default);

        var docs = h.Repo.All.OrderBy(d => d.Sha256).ToList();
        Assert.Equal(2, docs.Count);
        Assert.All(docs, d => Assert.Equal(TimestampStatuses.Granted, d.Status));
        Assert.Equal([TimestampHarness.Sha('a'), TimestampHarness.Sha('b')], docs.Select(d => d.Sha256).ToArray());
    }

    [Fact]
    public async Task InlineExitoso_Granted_YEventoGranted()
    {
        var h = new TimestampHarness();
        await h.Service.SealEvidenceAsync(h.Case, [("captura_1.png", TimestampHarness.Sha('a'))], default);

        var doc = Assert.Single(h.Repo.All);
        Assert.Equal(TimestampStatuses.Granted, doc.Status);
        Assert.Equal(1, doc.Attempts);
        Assert.Equal("Free TSA", doc.Authority);
        Assert.Equal("https://freetsa.org/tsr", doc.TsaUrl);
        Assert.Equal(FakeTsaClient.GenTime, doc.GenTime);
        Assert.Equal("0A1B2C", doc.SerialNumber);
        Assert.NotNull(doc.Tsr);
        Assert.Null(doc.LeaseId);
        Assert.Null(doc.NextAttemptAt);

        var evt = Assert.Single(h.Events.Events);
        Assert.Equal(CaseEventTypes.TimestampGranted, evt.Type);
        Assert.Equal("captura_1.png", evt.Filename);
        Assert.Equal(h.Case.Officer.Dni, evt.ActorDni);
        Assert.Equal(TimestampTargets.Evidence, evt.Detail!.Target);
        Assert.Equal(TimestampHarness.Sha('a'), evt.Detail.Sha256);
        Assert.Equal("Free TSA", evt.Detail.Authority);
        Assert.Equal(FakeTsaClient.GenTime, evt.Detail.GenTime);
        Assert.Equal("0A1B2C", evt.Detail.SerialNumber);
        Assert.Equal("https://freetsa.org/tsr", evt.Detail.TsaUrl);
        Assert.Equal(0, h.Signal.Wakes);
    }

    [Fact]
    public async Task InlineFallido_Pending_Intento1_Backoff_EventoYWake()
    {
        var h = new TimestampHarness();
        h.Tsa.Default = FakeTsaClient.Failed();
        var now = h.Time.GetUtcNow().UtcDateTime;
        await h.Service.SealEvidenceAsync(h.Case, [("captura_1.png", TimestampHarness.Sha('a'))], default);

        var doc = Assert.Single(h.Repo.All);
        Assert.Equal(TimestampStatuses.Pending, doc.Status);
        Assert.Equal(1, doc.Attempts);
        Assert.Equal(TimestampErrorKinds.Unreachable, doc.LastErrorKind);
        Assert.InRange((doc.NextAttemptAt!.Value - now).TotalSeconds, 54, 66); // 1 min ± 10 %
        Assert.Null(doc.LeaseId);

        var evt = Assert.Single(h.Events.Events);
        Assert.Equal(CaseEventTypes.TimestampFailed, evt.Type);
        Assert.Equal(1, evt.Detail!.Attempt);
        Assert.False(evt.Detail.Final);
        Assert.Equal("freetsa.org: tiempo agotado", evt.Detail.Reason);
        Assert.Equal(1, h.Signal.Wakes);
    }

    [Fact]
    public async Task Disyuntor_TrasUnFalloTotal_ElSiguienteNoLlamaALaTsa_YDespuesDelCooldownSi()
    {
        var h = new TimestampHarness();
        h.Tsa.Default = FakeTsaClient.Failed();
        await h.Service.SealEvidenceAsync(h.Case, [("a.png", TimestampHarness.Sha('a'))], default);
        Assert.Equal(1, h.Tsa.Calls);

        h.Time.Advance(TimeSpan.FromSeconds(60));
        await h.Service.SealEvidenceAsync(h.Case, [("b.png", TimestampHarness.Sha('b'))], default);
        Assert.Equal(1, h.Tsa.Calls); // no llamó: quedó pending para la cola
        var b = h.Repo.All.Single(d => d.Filename == "b.png");
        Assert.Equal(TimestampStatuses.Pending, b.Status);
        Assert.Equal(0, b.Attempts);
        Assert.Null(b.LeaseId); // sin lease: la cola lo puede tomar ya
        Assert.Equal(2, h.Signal.Wakes);

        h.Time.Advance(TimeSpan.FromSeconds(61));
        await h.Service.SealEvidenceAsync(h.Case, [("c.png", TimestampHarness.Sha('c'))], default);
        Assert.Equal(2, h.Tsa.Calls);
    }

    [Fact]
    public async Task VariosItems_UnFalloTotal_LosSiguientesVanALaCola()
    {
        var h = new TimestampHarness();
        h.Tsa.Default = FakeTsaClient.Failed();
        await h.Service.SealEvidenceAsync(h.Case,
            [("a.png", TimestampHarness.Sha('a')), ("b.png", TimestampHarness.Sha('b')), ("c.png", TimestampHarness.Sha('c'))],
            default);
        Assert.Equal(1, h.Tsa.Calls);
        Assert.Equal(3, h.Repo.All.Count);
        Assert.All(h.Repo.All, d => Assert.Equal(TimestampStatuses.Pending, d.Status));
    }

    [Fact]
    public async Task PresupuestoInline_SeComparteEntreLosItems()
    {
        var h = new TimestampHarness();
        await h.Service.SealEvidenceAsync(h.Case, [("a.png", TimestampHarness.Sha('a'))], default);
        var (_, budget) = Assert.Single(h.Tsa.Requests);
        Assert.InRange(budget.TotalSeconds, 9, 10);
    }

    [Fact]
    public async Task Desactivado_NoOp_SinDocumentos()
    {
        var h = new TimestampHarness(enabled: false);
        await h.Service.SealEvidenceAsync(h.Case, [("a.png", TimestampHarness.Sha('a'))], default);
        var now = await h.Service.SealNowAsync(h.Case, TimestampTargets.Zip, "evidencia_x.zip", TimestampHarness.Sha('z'),
            TimeSpan.FromSeconds(15));

        Assert.Null(now);
        Assert.Empty(h.Repo.All);
        Assert.Equal(0, h.Tsa.Calls);
        Assert.Null(h.Service.AuthorityHint);
        Assert.False(h.Service.IsEnabled);
    }

    [Fact]
    public void AuthorityHint_EsLaDeLaPrimeraUrl()
    {
        var h = new TimestampHarness();
        Assert.Equal("Free TSA", h.Service.AuthorityHint);
    }

    [Fact]
    public async Task SealNow_ZipGranted_MarcarEnInforme_YAbortarNoLoToca()
    {
        var h = new TimestampHarness();
        var zip = await h.Service.SealNowAsync(h.Case, TimestampTargets.Zip, "evidencia_x.zip", TimestampHarness.Sha('z'),
            TimeSpan.FromSeconds(15));
        Assert.Equal(TimestampStatuses.Granted, zip!.Status);

        await h.Service.MarkZipInReportAsync(zip.Id);
        await h.Service.AbortZipAsync(h.Case.Id, "evidencia_x.zip", TimestampHarness.Sha('z'));
        var doc = Assert.Single(h.Repo.All);
        Assert.True(doc.InReport);
        Assert.Equal(TimestampStatuses.Granted, doc.Status);
    }

    [Fact]
    public async Task SealNow_ZipPending_AbortarLoPasaAFailed_SinEvento()
    {
        var h = new TimestampHarness();
        h.Tsa.Default = FakeTsaClient.Failed();
        var zip = await h.Service.SealNowAsync(h.Case, TimestampTargets.Zip, "evidencia_x.zip", TimestampHarness.Sha('z'),
            TimeSpan.FromSeconds(15));
        Assert.Equal(TimestampStatuses.Pending, zip!.Status);
        var eventsBefore = h.Events.Events.Count;

        await h.Service.AbortZipAsync(h.Case.Id, "evidencia_x.zip", TimestampHarness.Sha('z'));
        var doc = Assert.Single(h.Repo.All);
        Assert.Equal(TimestampStatuses.Failed, doc.Status);
        Assert.Equal(TimestampService.AbortedZipMessage, doc.LastError);
        Assert.Null(doc.NextAttemptAt);
        Assert.Equal(eventsBefore, h.Events.Events.Count);
    }

    [Fact]
    public async Task SealNow_IgnoraElDisyuntor()
    {
        var h = new TimestampHarness();
        h.Tsa.Default = FakeTsaClient.Failed();
        await h.Service.SealEvidenceAsync(h.Case, [("a.png", TimestampHarness.Sha('a'))], default);
        await h.Service.SealNowAsync(h.Case, TimestampTargets.Zip, "evidencia_x.zip", TimestampHarness.Sha('z'),
            TimeSpan.FromSeconds(15));
        Assert.Equal(2, h.Tsa.Calls);
        Assert.Equal(TimeSpan.FromSeconds(15), h.Tsa.Requests[1].Budget);
    }

    [Fact]
    public async Task ListAsync_LeeAunqueEsteDesactivado()
    {
        var h = new TimestampHarness(enabled: false);
        h.Repo.Seed(new CaseTimestamp
        {
            CaseId = h.Case.Id, Target = TimestampTargets.Evidence, Filename = "a.png", Sha256 = TimestampHarness.Sha('a'),
            Status = TimestampStatuses.Granted, Tsr = [1, 2, 3],
        });
        var list = await h.Service.ListAsync(h.Case.Id, default);
        Assert.Null(Assert.Single(list).Tsr);
        Assert.NotNull(Assert.Single(await h.Service.ListAsync(h.Case.Id, default, includeTsr: true)).Tsr);
    }
}
