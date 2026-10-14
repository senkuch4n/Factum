using System.Reflection;
using System.Text.Json;
using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Factum.Backend.Services.Cases;
using Factum.Backend.Services.Catalogs;
using Factum.Backend.Services.Profile;
using Factum.Backend.Services.Reports;
using Factum.Backend.Services.Timestamping;
using Factum.Backend.Tests.Timestamping;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using static Factum.Backend.Tests.FichaTecnica.DeviceDetailsTestData;

namespace Factum.Backend.Tests.FichaTecnica;

/// <summary>Doble que tira en cualquier método: la ficha técnica no tiene que usar ese servicio.</summary>
public class NotUsedProxy : DispatchProxy
{
    public static T Create<T>() where T : class => DispatchProxy.Create<T, NotUsedProxy>();

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        // ResolveStorage arma el delegado de HasEvidenceFiles; un caso agent no lo invoca.
        throw new NotSupportedException($"La ficha técnica no usa {targetMethod?.DeclaringType?.Name}.{targetMethod?.Name}");
    }
}

/// <summary>
/// <see cref="ICaseRepository"/> en memoria para la ficha técnica: FindById y las 3 escrituras de §7.1, con el mismo
/// filtro de editabilidad del repositorio real. Guarda y devuelve copias por BSON (como el driver), así también se
/// prueba el mapeo de las clases nuevas.
/// </summary>
public class DeviceDetailsRepoProxy : DispatchProxy
{
    public Dictionary<string, BsonDocument> Docs { get; } = new(StringComparer.Ordinal);
    public int Writes { get; private set; }

    public static (ICaseRepository Repo, DeviceDetailsRepoProxy Proxy) Create(params Case[] cases)
    {
        var repo = DispatchProxy.Create<ICaseRepository, DeviceDetailsRepoProxy>();
        var proxy = (DeviceDetailsRepoProxy)(object)repo;
        foreach (var c in cases) proxy.Docs[c.Id] = c.ToBsonDocument();
        return (repo, proxy);
    }

    public Case Get(string id) => BsonSerializer.Deserialize<Case>(Docs[id]);

    private bool Editable(string id, bool requireDetails, out Case cas)
    {
        cas = Get(id);
        return cas.Status is not (CaseStatus.Generating or CaseStatus.Completed) &&
               (!requireDetails || cas.DeviceDetails is not null);
    }

    protected override object? Invoke(MethodInfo? m, object?[]? args)
    {
        var id = (string)args![0]!;
        switch (m!.Name)
        {
            case nameof(ICaseRepository.FindByIdAsync):
                return Task.FromResult<Case?>(Docs.ContainsKey(id) ? Get(id) : null);
            case nameof(ICaseRepository.SetDeviceDetailsAsync):
            {
                if (!Editable(id, false, out var cas)) return Task.FromResult(false);
                cas.DeviceDetails = BsonSerializer.Deserialize<Models.DeviceDetails>(((Models.DeviceDetails)args[1]!).ToBsonDocument());
                Docs[id] = cas.ToBsonDocument();
                Writes++;
                return Task.FromResult(true);
            }
            case nameof(ICaseRepository.SetClockCloseAsync):
            {
                if (!Editable(id, true, out var cas)) return Task.FromResult(false);
                cas.DeviceDetails!.ClockClose = (ClockMeasurement)args[1]!;
                Docs[id] = cas.ToBsonDocument();
                Writes++;
                return Task.FromResult(true);
            }
            case nameof(ICaseRepository.SetIncludeSsidAsync):
            {
                if (!Editable(id, true, out var cas)) return Task.FromResult(false);
                cas.DeviceDetails!.IncludeSsidInReport = (bool)args[1]!;
                Docs[id] = cas.ToBsonDocument();
                Writes++;
                return Task.FromResult(true);
            }
        }
        throw new NotSupportedException($"La ficha técnica no usa ICaseRepository.{m.Name}");
    }
}

// metadatos-dispositivo §7.1 y §13.3: PUT …/device-details, …/clock y …/options sobre CaseService con dobles.
public sealed class DeviceDetailsFlowTests
{
    private const string OfficerDni = "11111111";
    private static readonly JsonSerializerOptions Snake = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly RecordingCaseEventRepository _events = new();
    private readonly ICaseRepository _repo;
    private readonly DeviceDetailsRepoProxy _store;
    private readonly CaseService _svc;

    public DeviceDetailsFlowTests()
    {
        (_repo, _store) = DeviceDetailsRepoProxy.Create(NewCase("c1"), NewCase("c2", CaseStatus.Completed),
            NewCase("c3", CaseStatus.Error));
        _svc = new CaseService(_repo, NotUsedProxy.Create<IStorageService>(), NotUsedProxy.Create<IReportService>(),
            NotUsedProxy.Create<IExpertProfileService>(), new ReportTestSupport.FakeSettings(),
            NotUsedProxy.Create<ICatalogService>(), _events, new HttpContextAccessor(),
            NotUsedProxy.Create<ITimestampService>(), Options.Create(new StorageOptions()),
            Options.Create(new ReportOptions()), NullLogger<CaseService>.Instance);
    }

    private static Case NewCase(string id, CaseStatus status = CaseStatus.Draft) => new()
    {
        Id = id,
        Status = status,
        EvidenceStorage = EvidenceStorages.Agent,
        Officer = new User { Dni = OfficerDni, Name = "Perito Ficticio" },
        Device = new DeviceInfo { Serial = Serial, Manufacturer = "Samsung", Model = "SM-M127F", Platform = "android" },
    };

    private static PcServerProbeDto Probe()
    {
        var s = DateTimeOffset.UtcNow.AddMilliseconds(-200);
        return new PcServerProbeDto(s.AddMilliseconds(-60), s, s.AddMilliseconds(80));
    }

    private static SaveDeviceDetailsRequest Request(string? ssid = "Casa Ana", bool? include = null,
        string connection = Serial, string platform = "android") =>
        new(platform, connection, "1.7.0", AndroidFieldsDto(ssid), RealClock(), Probe(), include);

    private static string Code<T>(Result<T> r) => (string)r.Details!["code"]!;

    [Fact]
    public async Task Put_guarda_recalcula_y_emite_el_evento_sin_SSID()
    {
        // El cliente manda cifras falsas: la API las ignora y recalcula desde las horas crudas.
        var r = await _svc.SaveDeviceDetailsAsync("c1", Request(), OfficerDni);
        Assert.True(r.IsSuccess);
        var d = r.Value!.DeviceDetails!;
        Assert.Equal("android", d.Platform);
        Assert.Equal(Serial, d.ConnectionId);
        Assert.Equal("1.7.0", d.AgentVersion);
        Assert.Equal(DeviceDetailKeys.All.OrderBy(k => k), d.Fields.Keys.OrderBy(k => k));
        Assert.Equal("Casa Ana", d.Fields["wifi_ssid"].Value);
        Assert.False(d.IncludeSsidInReport);
        Assert.Equal(ClockKinds.Start, d.ClockStart!.Kind);
        Assert.Equal(10038, d.ClockStart.DeviceMinusPcMs);
        Assert.Equal(50, d.ClockStart.MarginMs);
        Assert.Equal(10, d.ClockStart.PcMinusServerMs); // mid = s + 10 ms
        Assert.Equal(71, d.ClockStart.PcServerMarginMs);
        Assert.Null(d.ClockStart.PcServerReason);
        Assert.Null(d.ClockClose);
        Assert.True((DateTime.UtcNow - d.ReadAt).Duration() < TimeSpan.FromMinutes(1));

        var evt = Assert.Single(_events.Events);
        Assert.Equal(CaseEventTypes.DeviceDetailsRecorded, evt.Type);
        Assert.Equal("c1", evt.CaseId);
        Assert.Equal(3, evt.Detail!.UnavailableCount);
        Assert.Null(evt.Detail.Reread);
        Assert.Equal("start", evt.Detail.ClockKind);
        Assert.Equal(10038, evt.Detail.DeviceMinusPcMs);
        Assert.Equal(50, evt.Detail.ClockMarginMs);
        Assert.Equal(10, evt.Detail.PcMinusServerMs);
        var json = JsonSerializer.Serialize(evt, Snake);
        Assert.DoesNotContain("Casa Ana", json);
        Assert.DoesNotContain("R58RA3R5AGA", json);
        Assert.DoesNotContain("orange", json);

        // El JSON del caso: device_details con las claves de §8.3.
        var caseJson = JsonSerializer.Serialize(r.Value, Snake);
        using var doc = JsonDocument.Parse(caseJson);
        var dd = doc.RootElement.GetProperty("device_details");
        foreach (var key in new[] { "read_at", "platform", "connection_id", "agent_version", "fields", "clock_start",
                     "clock_close", "include_ssid_in_report" })
            Assert.True(dd.TryGetProperty(key, out _), key);
        Assert.Equal(10038, dd.GetProperty("clock_start").GetProperty("device_minus_pc_ms").GetInt64());
        Assert.False(dd.GetProperty("fields").GetProperty("serial_number").TryGetProperty("reason", out _));
        Assert.Equal("empty", dd.GetProperty("fields").GetProperty("network_operator").GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Device_mismatch_y_datos_invalidos()
    {
        var mismatch = await _svc.SaveDeviceDetailsAsync("c1", Request(connection: "OTRO"), OfficerDni);
        Assert.Equal(ErrorKind.Conflict, mismatch.Kind);
        Assert.Equal("device_mismatch", Code(mismatch));
        Assert.Equal("El dispositivo conectado no es el del caso.", mismatch.Error);

        var badPlatform = await _svc.SaveDeviceDetailsAsync("c1", Request(platform: "windows"), OfficerDni);
        Assert.Equal(ErrorKind.Validation, badPlatform.Kind);
        Assert.Equal("invalid_device_details", Code(badPlatform));

        var noConn = await _svc.SaveDeviceDetailsAsync("c1", Request(connection: "  "), OfficerDni);
        Assert.Equal("invalid_device_details", Code(noConn));

        Assert.Equal(0, _store.Writes);
        Assert.Empty(_events.Events);
    }

    [Fact]
    public async Task Caso_no_editable_409_y_ajeno_404()
    {
        var done = await _svc.SaveDeviceDetailsAsync("c2", Request(), OfficerDni);
        Assert.Equal(ErrorKind.Conflict, done.Kind);
        Assert.Equal("case_not_editable", Code(done));

        var foreign = await _svc.SaveDeviceDetailsAsync("c1", Request(), "99999999");
        Assert.Equal(ErrorKind.NotFound, foreign.Kind);

        // "error" es editable (borrador = draft o error).
        Assert.True((await _svc.SaveDeviceDetailsAsync("c3", Request(), OfficerDni)).IsSuccess);
    }

    [Fact]
    public async Task Segundo_put_es_reread_y_conserva_flag_y_clock_close()
    {
        Assert.True((await _svc.SaveDeviceDetailsAsync("c1", Request(), OfficerDni)).IsSuccess);
        Assert.True((await _svc.SetDeviceDetailsOptionsAsync("c1", new DeviceDetailsOptionsRequest(true), OfficerDni))
            .IsSuccess);
        Assert.True((await _svc.SaveDeviceClockAsync("c1",
            new SaveDeviceClockRequest(Serial, "1.7.0", RealClock(), null), OfficerDni)).IsSuccess);

        var second = await _svc.SaveDeviceDetailsAsync("c1", Request(include: null), OfficerDni);
        Assert.True(second.IsSuccess);
        var d = second.Value!.DeviceDetails!;
        Assert.True(d.IncludeSsidInReport);
        Assert.NotNull(d.ClockClose);
        Assert.Equal(ClockKinds.Close, d.ClockClose!.Kind);

        var recorded = _events.Events.Where(e => e.Type == CaseEventTypes.DeviceDetailsRecorded).ToList();
        Assert.Equal(2, recorded.Count);
        Assert.Null(recorded[0].Detail!.Reread);
        Assert.True(recorded[1].Detail!.Reread);

        // Con include_ssid_in_report explícito, gana el del request.
        var third = await _svc.SaveDeviceDetailsAsync("c1", Request(include: false), OfficerDni);
        Assert.False(third.Value!.DeviceDetails!.IncludeSsidInReport);
    }

    [Fact]
    public async Task Clock_sin_ficha_es_no_device_details_y_con_ficha_emite_el_evento()
    {
        var none = await _svc.SaveDeviceClockAsync("c1", new SaveDeviceClockRequest(Serial, "1.7.0", RealClock(), Probe()),
            OfficerDni);
        Assert.Equal(ErrorKind.Conflict, none.Kind);
        Assert.Equal("no_device_details", Code(none));
        Assert.Equal("El caso no tiene ficha técnica registrada.", none.Error);

        Assert.True((await _svc.SaveDeviceDetailsAsync("c1", Request(), OfficerDni)).IsSuccess);
        var mismatch = await _svc.SaveDeviceClockAsync("c1",
            new SaveDeviceClockRequest("OTRO", "1.7.0", RealClock(), Probe()), OfficerDni);
        Assert.Equal("device_mismatch", Code(mismatch));

        var ok = await _svc.SaveDeviceClockAsync("c1",
            new SaveDeviceClockRequest(Serial, "1.7.0", new DeviceClockDto(Reason: "timeout"), null), OfficerDni);
        Assert.True(ok.IsSuccess);
        var close = ok.Value!.DeviceDetails!.ClockClose!;
        Assert.Equal("timeout", close.Reason);
        Assert.Null(close.DeviceMinusPcMs);
        Assert.Equal("not_measured", close.PcServerReason);

        var evt = _events.Events.Last();
        Assert.Equal(CaseEventTypes.DeviceClockMeasured, evt.Type);
        Assert.Equal("close", evt.Detail!.ClockKind);
        Assert.Equal("timeout", evt.Detail.Reason);
        Assert.Null(evt.Detail.DeviceMinusPcMs);
        Assert.Null(evt.Detail.UnavailableCount);
    }

    [Fact]
    public async Task Options_cambia_el_flag_sin_evento()
    {
        var none = await _svc.SetDeviceDetailsOptionsAsync("c1", new DeviceDetailsOptionsRequest(true), OfficerDni);
        Assert.Equal("no_device_details", Code(none));

        Assert.True((await _svc.SaveDeviceDetailsAsync("c1", Request(), OfficerDni)).IsSuccess);
        var before = _events.Events.Count;
        var missing = await _svc.SetDeviceDetailsOptionsAsync("c1", new DeviceDetailsOptionsRequest(), OfficerDni);
        Assert.Equal("invalid_device_details", Code(missing));

        var on = await _svc.SetDeviceDetailsOptionsAsync("c1", new DeviceDetailsOptionsRequest(true), OfficerDni);
        Assert.True(on.Value!.DeviceDetails!.IncludeSsidInReport);
        Assert.Equal(before, _events.Events.Count);
    }

    [Fact]
    public void Medicion_de_cierre_de_un_intento_anterior_no_entra_al_informe()
    {
        var created = new DateTime(2026, 10, 14, 16, 0, 0, DateTimeKind.Utc);
        var cas = NewCase("x");
        cas.DeviceDetails = Android();
        cas.DeviceDetails.ClockClose = new ClockMeasurement { Kind = "close", RecordedAt = created.AddSeconds(-1) };
        CaseService.DropStaleClockClose(cas, created);
        Assert.Null(cas.DeviceDetails.ClockClose);

        cas.DeviceDetails.ClockClose = new ClockMeasurement { Kind = "close", RecordedAt = created.AddSeconds(1) };
        CaseService.DropStaleClockClose(cas, created);
        Assert.NotNull(cas.DeviceDetails.ClockClose);

        var old = NewCase("y");
        CaseService.DropStaleClockClose(old, created); // sin ficha: no pasa nada
        Assert.Null(old.DeviceDetails);
    }

    [Fact]
    public void Documento_viejo_sin_DeviceDetails_deserializa_null()
    {
        var doc = NewCase("viejo").ToBsonDocument();
        Assert.False(doc.Contains("DeviceDetails"));
        Assert.Null(BsonSerializer.Deserialize<Case>(doc).DeviceDetails);
        // Y un rollback del binario tolera campos desconocidos dentro de la ficha.
        var withFicha = NewCase("nuevo");
        withFicha.DeviceDetails = Android();
        var bson = withFicha.ToBsonDocument();
        bson["DeviceDetails"].AsBsonDocument["CampoFuturo"] = 1;
        bson["DeviceDetails"]["ClockStart"].AsBsonDocument["OtroFuturo"] = "x";
        Assert.Equal(10038, BsonSerializer.Deserialize<Case>(bson).DeviceDetails!.ClockStart!.DeviceMinusPcMs);
    }
}
