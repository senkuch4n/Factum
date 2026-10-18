using System.Reflection;
using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Factum.Backend.Services.Cases;
using Factum.Backend.Services.Catalogs;
using Factum.Backend.Services.Profile;
using Factum.Backend.Services.Recording;
using Factum.Backend.Services.Reports;
using Factum.Backend.Services.Timestamping;
using Factum.Backend.Tests.FichaTecnica;
using Factum.Backend.Tests.Timestamping;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Factum.Backend.Tests.SesionGrabacion;

/// <summary>
/// <see cref="ICaseRepository"/> en memoria para la grabación de la sesión: FindById y las 3 escrituras de §6.3 con los
/// mismos filtros del repositorio real (editable, existencia, <c>SyncedAt</c>). Guarda copias por BSON, así también se
/// prueba el mapeo.
/// </summary>
public class SessionRepoProxy : DispatchProxy
{
    public Dictionary<string, BsonDocument> Docs { get; } = new(StringComparer.Ordinal);
    public int Writes { get; private set; }
    /// <summary>Cuántas escrituras del sync fallan a propósito (carrera simulada).</summary>
    public int FailSyncWrites { get; set; }

    public static (ICaseRepository Repo, SessionRepoProxy Proxy) Create(params Case[] cases)
    {
        var repo = DispatchProxy.Create<ICaseRepository, SessionRepoProxy>();
        var proxy = (SessionRepoProxy)(object)repo;
        foreach (var c in cases) proxy.Docs[c.Id] = c.ToBsonDocument();
        return (repo, proxy);
    }

    public Case Get(string id) => BsonSerializer.Deserialize<Case>(Docs[id]);

    private static bool Editable(Case c) => c.Status is not (CaseStatus.Generating or CaseStatus.Completed);

    protected override object? Invoke(MethodInfo? m, object?[]? args)
    {
        var id = (string)args![0]!;
        switch (m!.Name)
        {
            case nameof(ICaseRepository.FindByIdAsync):
                return Task.FromResult<Case?>(Docs.ContainsKey(id) ? Get(id) : null);
            case nameof(ICaseRepository.CreateRecordingSessionAsync):
            {
                var cas = Get(id);
                if (!Editable(cas) || cas.RecordingSession is not null) return Task.FromResult(false);
                cas.RecordingSession = (Models.RecordingSession)args[1]!;
                Docs[id] = cas.ToBsonDocument();
                Writes++;
                return Task.FromResult(true);
            }
            case nameof(ICaseRepository.SetRecordingSessionSettingsAsync):
            {
                var cas = Get(id);
                if (!Editable(cas) || cas.RecordingSession is null) return Task.FromResult(false);
                cas.RecordingSession.Enabled = (bool)args[1]!;
                cas.RecordingSession.WithCamera = (bool)args[2]!;
                cas.RecordingSession.ModeAtChange = (string)args[3]!;
                cas.RecordingSession.SettingsUpdatedAt = (DateTime)args[4]!;
                Docs[id] = cas.ToBsonDocument();
                Writes++;
                return Task.FromResult(true);
            }
            case nameof(ICaseRepository.SetRecordingSessionAsync):
            {
                var cas = Get(id);
                var expected = (DateTime?)args[1];
                if (!Editable(cas) || cas.RecordingSession is null || cas.RecordingSession.SyncedAt != expected)
                    return Task.FromResult(false);
                if (FailSyncWrites > 0)
                {
                    FailSyncWrites--;
                    return Task.FromResult(false);
                }
                var s = (Models.RecordingSession)args[2]!;
                s.Enabled = cas.RecordingSession.Enabled; // el sync nunca toca el switch
                s.WithCamera = cas.RecordingSession.WithCamera;
                cas.RecordingSession = s;
                Docs[id] = cas.ToBsonDocument();
                Writes++;
                return Task.FromResult(true);
            }
        }
        throw new NotSupportedException($"La grabación de la sesión no usa ICaseRepository.{m.Name}");
    }
}

/// <summary>Switch, sync y guardias del backend (grabacion-sesion-completa §6.3, §6.5) sobre CaseService con dobles.</summary>
public sealed class RecordingSessionFlowTests
{
    private const string Dni = "11111111";
    private readonly RecordingCaseEventRepository _events = new();
    private ICaseRepository _repo = null!;
    private SessionRepoProxy _store = null!;

    private static Case NewCase(string id, CaseStatus status = CaseStatus.Draft) => new()
    {
        Id = id,
        Status = status,
        SchemaVersion = 1,
        EvidenceStorage = EvidenceStorages.Agent,
        Officer = new User { Dni = Dni, Name = "Perito Ficticio" },
        Device = new DeviceInfo { Serial = "R58RA3R5AGA", Platform = "android" },
        EvidenceHost = new EvidenceHost { Hostname = "PC-1" },
    };

    private CaseService Service(string mode = "optional_off", params Case[] cases)
    {
        (_repo, _store) = SessionRepoProxy.Create(cases.Length > 0 ? cases : [NewCase("c1"), NewCase("c2", CaseStatus.Completed)]);
        return new CaseService(_repo, NotUsedProxy.Create<IStorageService>(), NotUsedProxy.Create<IReportService>(),
            NotUsedProxy.Create<IExpertProfileService>(), new ReportTestSupport.FakeSettings(),
            NotUsedProxy.Create<ICatalogService>(), _events, new HttpContextAccessor(),
            NotUsedProxy.Create<ITimestampService>(), Options.Create(new StorageOptions()),
            Options.Create(new ReportOptions()), NullLogger<CaseService>.Instance,
            new RecordingSessionOptions(mode, 20));
    }

    private static string? Code<T>(Result<T> r) => r.Details?["code"] as string;

    [Fact]
    public async Task Settings_CreaYEmiteSoloSiCambia()
    {
        var svc = Service();
        var r = await svc.SaveRecordingSessionSettingsAsync("c1", new RecordingSessionSettingsRequest(true, false), Dni);
        Assert.True(r.IsSuccess);
        var rs = r.Value!.RecordingSession!;
        Assert.True(rs.Enabled);
        Assert.Equal("optional_off", rs.ModeAtChange);
        Assert.Equal(20, rs.SegmentMinutes);
        var evt = Assert.Single(_events.Events);
        Assert.Equal("recording_session_settings_changed", evt.Type);
        Assert.True(evt.Detail!.Enabled);
        Assert.False(evt.Detail.WithCamera);

        await svc.SaveRecordingSessionSettingsAsync("c1", new RecordingSessionSettingsRequest(true, false), Dni);
        Assert.Single(_events.Events); // sin cambios: sin evento ni escritura
        Assert.Equal(1, _store.Writes);

        await svc.SaveRecordingSessionSettingsAsync("c1", new RecordingSessionSettingsRequest(false, false), Dni);
        Assert.Equal(2, _events.Events.Count);
        Assert.False(_store.Get("c1").RecordingSession!.Enabled);
    }

    [Fact]
    public async Task Settings_PreviousEnabledYPreviousWithCamera_F2()
    {
        var svc = Service();
        await svc.SaveRecordingSessionSettingsAsync("c1", new RecordingSessionSettingsRequest(true, false), Dni);
        var created = _events.Events[^1].Detail!;
        Assert.Null(created.PreviousEnabled); // el sub-documento se creó en ese pedido
        Assert.Null(created.PreviousWithCamera);

        await svc.SaveRecordingSessionSettingsAsync("c1", new RecordingSessionSettingsRequest(true, true), Dni);
        var sumo = _events.Events[^1].Detail!;
        Assert.Equal((true, true, true, false), (sumo.Enabled, sumo.WithCamera, sumo.PreviousEnabled, sumo.PreviousWithCamera));

        await svc.SaveRecordingSessionSettingsAsync("c1", new RecordingSessionSettingsRequest(true, false), Dni);
        var quito = _events.Events[^1].Detail!;
        Assert.Equal((true, false, true, true), (quito.Enabled, quito.WithCamera, quito.PreviousEnabled, quito.PreviousWithCamera));
        Assert.Equal(3, _events.Events.Count);
    }

    [Fact]
    public async Task Settings_EnabledFalse_GuardaWithCameraFalse_F2()
    {
        var svc = Service();
        await svc.SaveRecordingSessionSettingsAsync("c1", new RecordingSessionSettingsRequest(true, true), Dni);
        Assert.True(_store.Get("c1").RecordingSession!.WithCamera);

        await svc.SaveRecordingSessionSettingsAsync("c1", new RecordingSessionSettingsRequest(false, true), Dni);
        var rs = _store.Get("c1").RecordingSession!;
        Assert.False(rs.Enabled);
        Assert.False(rs.WithCamera); // D9 = A: no hay cámara sin sesión
        var off = _events.Events[^1].Detail!;
        Assert.Equal((false, false, true, true), (off.Enabled, off.WithCamera, off.PreviousEnabled, off.PreviousWithCamera));
    }

    [Fact]
    public async Task Settings_ModosDeLaInstalacion()
    {
        var disabled = Service("disabled");
        var on = await disabled.SaveRecordingSessionSettingsAsync("c1", new RecordingSessionSettingsRequest(true), Dni);
        Assert.Equal(ErrorKind.Conflict, on.Kind);
        Assert.Equal("recording_session_disabled", Code(on));
        Assert.True((await disabled.SaveRecordingSessionSettingsAsync("c1", new RecordingSessionSettingsRequest(false), Dni)).IsSuccess);

        var required = Service("required");
        var off = await required.SaveRecordingSessionSettingsAsync("c1", new RecordingSessionSettingsRequest(false), Dni);
        Assert.Equal(ErrorKind.Validation, off.Kind);
        Assert.Equal("recording_session_required", Code(off));

        var missing = await required.SaveRecordingSessionSettingsAsync("c1", new RecordingSessionSettingsRequest(), Dni);
        Assert.Equal("invalid_recording_session", Code(missing));
        Assert.Equal("enabled", missing.Details!["field"]);

        var completed = await required.SaveRecordingSessionSettingsAsync("c2", new RecordingSessionSettingsRequest(true), Dni);
        Assert.Equal("case_not_editable", Code(completed));
        var foreign = await required.SaveRecordingSessionSettingsAsync("c1", new RecordingSessionSettingsRequest(true), "otro");
        Assert.Equal(ErrorKind.NotFound, foreign.Kind);
    }

    [Fact]
    public async Task Sync_RequiereSwitch_YMergeaConEventos()
    {
        var svc = Service();
        var notEnabled = await svc.SyncRecordingSessionAsync("c1", SessionData.Snapshot(), Dni);
        Assert.Equal("recording_session_not_enabled", Code(notEnabled));

        await svc.SaveRecordingSessionSettingsAsync("c1", new RecordingSessionSettingsRequest(true), Dni);
        _events.Events.Clear();
        var ok = await svc.SyncRecordingSessionAsync("c1", SessionData.Snapshot(state: "stopped"), Dni);
        Assert.True(ok.IsSuccess);
        var rs = ok.Value!.RecordingSession!;
        Assert.True(rs.Enabled);
        Assert.Equal(SessionData.SessionId, rs.SessionId);
        Assert.Equal("R58RA3R5AGA", rs.ConnectionId);
        Assert.NotNull(rs.SyncedAt);
        Assert.Single(rs.Segments);
        Assert.Equal(["recording_session_started", "recording_session_segment_closed"], _events.Events.Select(e => e.Type));
        Assert.Equal(SessionData.Segment(1).Filename, _events.Events[1].Filename);

        // Idéntico: sin escritura ni eventos.
        var writes = _store.Writes;
        await svc.SyncRecordingSessionAsync("c1", SessionData.Snapshot(state: "stopped"), Dni);
        Assert.Equal(writes, _store.Writes);
        Assert.Equal(2, _events.Events.Count);

        var invalid = await svc.SyncRecordingSessionAsync("c1", SessionData.Snapshot() with { Platform = "x" }, Dni);
        Assert.Equal("invalid_recording_session", Code(invalid));
        Assert.Equal("platform", invalid.Details!["field"]);

        var conflict = await svc.SyncRecordingSessionAsync("c1",
            SessionData.Snapshot([SessionData.Segment(1, sha: 'c')]), Dni);
        Assert.Equal("recording_session_conflict", Code(conflict));
    }

    [Fact]
    public async Task Sync_ReintentaYDespuesBusy()
    {
        var svc = Service();
        await svc.SaveRecordingSessionSettingsAsync("c1", new RecordingSessionSettingsRequest(true), Dni);
        _store.FailSyncWrites = 2;
        Assert.True((await svc.SyncRecordingSessionAsync("c1", SessionData.Snapshot(), Dni)).IsSuccess);
        _store.FailSyncWrites = 3;
        var busy = await svc.SyncRecordingSessionAsync("c1", SessionData.Snapshot([SessionData.Segment(1), SessionData.Segment(2, sha: 'b')]), Dni);
        Assert.Equal("recording_session_busy", Code(busy));
    }

    [Fact]
    public async Task Prepare_ConSesionAbiertaOTramosSinRegistrar_409()
    {
        var cas = NewCase("c1");
        cas.Evidence.Add(new EvidenceItem { Filename = "screenshot_1.png", Size = 10, Sha256 = new string('1', 64) });
        cas.RecordingSession = SessionData.Enabled();
        cas.RecordingSession.State = "recording";
        var svc = Service("optional_off", cas);
        var open = await svc.PrepareGenerationAsync("c1", new PrepareGenerationRequest("PC-1"), Dni);
        Assert.Equal("recording_session_open", Code(open));
        Assert.Equal(["<sesion abierta>"], (List<string>)open.Details!["pending_files"]!);
        Assert.Equal("Primero cerrá la grabación de la sesión.", open.Error);

        var c2 = NewCase("c1");
        c2.Evidence.Add(new EvidenceItem { Filename = "screenshot_1.png", Size = 10, Sha256 = new string('1', 64) });
        c2.RecordingSession = RecordingSessionRules.Merge(SessionData.Enabled(),
            SessionData.Valid(SessionData.Snapshot(state: "stopped"))).Merged;
        svc = Service("optional_off", c2);
        var pending = await svc.PrepareGenerationAsync("c1", new PrepareGenerationRequest("PC-1"), Dni);
        Assert.Equal([SessionData.Segment(1).Filename!], (List<string>)pending.Details!["pending_files"]!);
    }

    [Fact]
    public async Task DeleteEvidence_DeUnTramo_409()
    {
        var cas = NewCase("c1");
        var name = SessionData.Segment(1).Filename!;
        cas.Evidence.Add(new EvidenceItem { Filename = name, Size = 943718400, Sha256 = SessionData.Sha('a') });
        cas.RecordingSession = RecordingSessionRules.Merge(SessionData.Enabled(),
            SessionData.Valid(SessionData.Snapshot(state: "stopped"))).Merged;
        var svc = Service("optional_off", cas);
        var r = await svc.DeleteEvidenceAsync("c1", name, Dni);
        Assert.Equal("recording_session_file", Code(r));
        Assert.Equal("Los tramos de la grabación de la sesión no se pueden quitar del caso.", r.Error);
    }

    [Fact]
    public async Task Sync_HorasConMicrosegundos_SegundoSyncIdempotente()
    {
        // QA #80 H-1: Tatana emite ticks (…:10.4813457Z); Mongo guarda ms. El segundo sync idéntico tiene que dar 200
        // sin escritura ni eventos, no 409.
        var svc = Service();
        await svc.SaveRecordingSessionSettingsAsync("c1", new RecordingSessionSettingsRequest(true), Dni);
        var t = SessionData.T0.AddTicks(4567); // 0,4567 ms de más
        var seg = SessionData.Segment(1) with { StartedAt = t, StoppedAt = t.AddSeconds(1800.7).AddTicks(123) };
        var gap = SessionData.Gap(1, t.AddSeconds(1800.7).AddTicks(9), t.AddSeconds(1900).AddTicks(77));
        var openGap = SessionData.Gap(2, t.AddSeconds(4000).AddTicks(5));
        var mark = SessionData.Mark(1) with { At = t.AddSeconds(513.6).AddTicks(31) };
        var snap = SessionData.Snapshot([seg], [gap, openGap], [mark], state: "stopped");

        Assert.True((await svc.SyncRecordingSessionAsync("c1", snap, Dni)).IsSuccess);
        var writes = _store.Writes;
        var events = _events.Events.Count;
        var again = await svc.SyncRecordingSessionAsync("c1", snap, Dni);
        Assert.True(again.IsSuccess, again.Error);
        Assert.Equal(writes, _store.Writes);
        Assert.Equal(events, _events.Events.Count);
        var rs = again.Value!.RecordingSession!;
        Assert.Equal(0, rs.Marks[0].At.Ticks % TimeSpan.TicksPerMillisecond);
        Assert.Equal(0, rs.Segments[0].StartedAt.Ticks % TimeSpan.TicksPerMillisecond);

        // Y el hueco abierto se cierra en un sync posterior sin conflicto.
        var closed = SessionData.Snapshot([seg], [gap, openGap with { To = t.AddSeconds(4100).AddTicks(3) }], [mark], state: "stopped");
        Assert.True((await svc.SyncRecordingSessionAsync("c1", closed, Dni)).IsSuccess);
    }

    [Fact]
    public async Task DeleteEvidence_TramoTodaviaNoSincronizado_409()
    {
        // QA #80 H-1: sesión en curso, tramo registrado como evidencia pero todavía no en la copia del diario.
        var cas = NewCase("c1");
        var name = SessionData.Segment(1).Filename!;
        cas.Evidence.Add(new EvidenceItem { Filename = name, Size = 10, Sha256 = SessionData.Sha('a') });
        cas.RecordingSession = SessionData.Enabled();
        cas.RecordingSession.State = "recording";
        var r = await Service("optional_off", cas).DeleteEvidenceAsync("c1", name, Dni);
        Assert.Equal("recording_session_file", Code(r));
    }

    [Fact]
    public async Task GetCaso_TraeRecordingSession()
    {
        // QA #80 H-2: el detalle carga GET /api/cases/{id} (sin la proyección del listado).
        var cas = NewCase("c1");
        cas.RecordingSession = RecordingSessionRules.Merge(SessionData.Enabled(),
            SessionData.Valid(SessionData.Snapshot(state: "stopped"))).Merged;
        var r = await Service("optional_off", cas).GetAsync("c1", Dni);
        Assert.True(r.IsSuccess);
        Assert.Single(r.Value.Case.RecordingSession!.Segments);
    }

    [Fact]
    public void Bson_CasoViejoSinElCampo_EsNull()
    {
        var doc = NewCase("viejo").ToBsonDocument();
        Assert.False(doc.Contains("RecordingSession"));
        Assert.Null(BsonSerializer.Deserialize<Case>(doc).RecordingSession);
    }
}
