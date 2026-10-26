using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Extractions;
using Factum.Backend.Tests.Timestamping;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Factum.Backend.Tests.Extractions;

public class ExtractionServiceTests
{
    private static readonly User Ana = new() { Dni = "30111222", Name = "Ana Pérez" };
    private static readonly DateTime Now = new(2026, 10, 25, 18, 0, 0, DateTimeKind.Utc);
    private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private sealed class Harness
    {
        public FakeExtractionRepository Repo { get; } = new();
        public FakeCaseEventRepository Events { get; } = new();
        public FakeTimestampService Timestamps { get; }
        public FakeCaseTimestampRepository TimestampRepo { get; } = new();
        public ExtractionService Service { get; }

        public Harness(bool sealingEnabled = false, params Case[] cases)
        {
            Timestamps = new FakeTimestampService { Enabled = sealingEnabled };
            var (caseRepo, _) = CaseLookupProxy.Create(cases);
            Service = new ExtractionService(Repo, caseRepo, Events, TimestampRepo, Timestamps,
                new ReportTestSupport.FakeSettings(),
                new FakeTimeProvider(Now), new HttpContextAccessor(), NullLogger<ExtractionService>.Instance);
        }
    }

    private sealed class FakeTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    }

    private static string NewId() => Guid.NewGuid().ToString("D");

    private static UpsertExtractionRequest Running() => new(
        Status: "running",
        Device: new ExtractionDeviceDto(Serial: "R58", Manufacturer: "samsung", Model: "SM-M127F"),
        StartedAtPc: new DateTimeOffset(Now));

    private static UpsertExtractionRequest Completed() => Running() with
    {
        Status = "completed",
        FinishedAtPc = new DateTimeOffset(Now.AddMinutes(10)),
        ManifestSha256 = Sha,
    };

    [Fact]
    public async Task Upsert_CreaConOfficerDelJwt()
    {
        var h = new Harness();
        var id = NewId();
        var r = await h.Service.UpsertAsync(id, Running(), Ana, default);
        Assert.True(r.IsSuccess);
        Assert.Equal("30111222", h.Repo.Store[id].Officer.Dni);
    }

    [Fact]
    public async Task Upsert_OtroDueño_NotFound()
    {
        var h = new Harness();
        var id = NewId();
        h.Repo.Store[id] = new Extraction { Id = id, Officer = new User { Dni = "99999999" }, Status = "running" };
        var r = await h.Service.UpsertAsync(id, Running(), Ana, default);
        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorKind.NotFound, r.Kind);
    }

    [Fact]
    public async Task Upsert_SellaElManifiestoUnaSolaVez()
    {
        var h = new Harness(sealingEnabled: true);
        var id = NewId();
        await h.Service.UpsertAsync(id, Running(), Ana, default);            // sin hash → no sella
        Assert.Equal(0, h.Timestamps.SealManifestCalls);
        await h.Service.UpsertAsync(id, Completed(), Ana, default);          // fija el hash → sella 1
        Assert.Equal(1, h.Timestamps.SealManifestCalls);
        await h.Service.UpsertAsync(id, Completed(), Ana, default);          // idempotente → no vuelve a sellar
        Assert.Equal(1, h.Timestamps.SealManifestCalls);
    }

    private static Case OwnedCase(string id, string referencia = "123/2026") => new()
    {
        Id = id,
        NroReferencia = referencia,
        Officer = Ana,
        Status = CaseStatus.Draft,
    };

    private async Task<(Harness H, string ExtId, string CaseId)> WithTerminalAndCase()
    {
        var caseId = NewId();
        var h = new Harness(cases: OwnedCase(caseId));
        var extId = NewId();
        await h.Service.UpsertAsync(extId, Completed(), Ana, default);
        return (h, extId, caseId);
    }

    [Fact]
    public async Task Link_RegistraEventoConExtractionIdShaYLabel()
    {
        var (h, extId, caseId) = await WithTerminalAndCase();
        var r = await h.Service.LinkAsync(extId, caseId, Ana, default);
        Assert.True(r.IsSuccess);
        Assert.Equal(caseId, r.Value!.CaseId);
        Assert.Equal("123/2026", r.Value.CaseRef);

        var evt = Assert.Single(h.Events.Events);
        Assert.Equal(CaseEventTypes.ExtractionLinked, evt.Type);
        Assert.Equal(extId, evt.Detail!.ExtractionId);
        Assert.Equal(Sha, evt.Detail.Sha256);
        Assert.Equal("samsung SM-M127F", evt.Detail.DeviceLabel);
    }

    [Fact]
    public async Task Link_AOtroCasoConUnoYaAsignado_AlreadyLinked()
    {
        var caseId1 = NewId();
        var caseId2 = NewId();
        var h = new Harness(cases: [OwnedCase(caseId1), OwnedCase(caseId2)]);
        var extId = NewId();
        await h.Service.UpsertAsync(extId, Completed(), Ana, default);
        await h.Service.LinkAsync(extId, caseId1, Ana, default);

        var r = await h.Service.LinkAsync(extId, caseId2, Ana, default);
        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorKind.Conflict, r.Kind);
        Assert.Equal(ExtractionService.CodeAlreadyLinked, r.Details!["code"]);
    }

    [Fact]
    public async Task Link_MismoCaso_Idempotente()
    {
        var (h, extId, caseId) = await WithTerminalAndCase();
        await h.Service.LinkAsync(extId, caseId, Ana, default);
        h.Events.Events.Clear();
        var r = await h.Service.LinkAsync(extId, caseId, Ana, default);
        Assert.True(r.IsSuccess);
        Assert.Empty(h.Events.Events); // sin evento nuevo
    }

    [Fact]
    public async Task Link_CasoAjeno_NotFound()
    {
        var h = new Harness(cases: new Case { Id = "ajeno", Officer = new User { Dni = "99999999" } });
        var extId = NewId();
        await h.Service.UpsertAsync(extId, Completed(), Ana, default);
        var r = await h.Service.LinkAsync(extId, "ajeno", Ana, default);
        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorKind.NotFound, r.Kind);
    }

    [Fact]
    public async Task Link_Running_NotFinished()
    {
        var caseId = NewId();
        var h = new Harness(cases: OwnedCase(caseId));
        var extId = NewId();
        await h.Service.UpsertAsync(extId, Running(), Ana, default);
        var r = await h.Service.LinkAsync(extId, caseId, Ana, default);
        Assert.False(r.IsSuccess);
        Assert.Equal(ExtractionService.CodeNotFinished, r.Details!["code"]);
    }

    [Fact]
    public async Task Unlink_RegistraEventoYLimpiaCaso()
    {
        var (h, extId, caseId) = await WithTerminalAndCase();
        await h.Service.LinkAsync(extId, caseId, Ana, default);
        h.Events.Events.Clear();

        var r = await h.Service.UnlinkAsync(extId, Ana, default);
        Assert.True(r.IsSuccess);
        Assert.Null(r.Value!.CaseId);
        var evt = Assert.Single(h.Events.Events);
        Assert.Equal(CaseEventTypes.ExtractionUnlinked, evt.Type);
        Assert.Equal(caseId, evt.CaseId);
    }

    [Fact]
    public async Task Unlink_SinCaso_Idempotente()
    {
        var (h, extId, _) = await WithTerminalAndCase();
        var r = await h.Service.UnlinkAsync(extId, Ana, default);
        Assert.True(r.IsSuccess);
        Assert.Empty(h.Events.Events);
    }

    [Fact]
    public async Task List_UnlinkedFiltra()
    {
        var caseId = NewId();
        var h = new Harness(cases: OwnedCase(caseId));
        var linked = NewId();
        var unlinked = NewId();
        await h.Service.UpsertAsync(linked, Completed(), Ana, default);
        await h.Service.LinkAsync(linked, caseId, Ana, default);
        await h.Service.UpsertAsync(unlinked, Completed(), Ana, default);

        var all = await h.Service.ListAsync(Ana, null, false, default);
        Assert.Equal(2, all.Value!.Count);

        var onlyUnlinked = await h.Service.ListAsync(Ana, null, true, default);
        Assert.Single(onlyUnlinked.Value!);
        Assert.Equal(unlinked, onlyUnlinked.Value![0].Id);
    }
}
