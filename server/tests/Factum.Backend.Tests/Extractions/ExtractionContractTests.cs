using Factum.Backend.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Extractions;

namespace Factum.Backend.Tests.Extractions;

/// <summary>
/// extraccion-logica-android §12.2 (D-T2): el `record` que arma Tatana (fixture compartido
/// <c>server/tests/Fixtures/extraction-record.json</c>) deserializa en <see cref="UpsertExtractionRequest"/> con
/// todos los campos. Mismo JSON que usa (usará) el test del lado de Tatana, para que el contrato no se desincronice.
/// Usa el MISMO naming (SnakeCaseLower) que la API (Program.cs).
/// </summary>
public class ExtractionContractTests
{
    private static readonly JsonSerializerOptions ApiJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private static UpsertExtractionRequest Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "extraction-record.json");
        return JsonSerializer.Deserialize<UpsertExtractionRequest>(File.ReadAllText(path), ApiJson)!;
    }

    [Fact]
    public void Record_Deserializa_TodosLosCampos()
    {
        var r = Load();
        Assert.Equal("completed_with_errors", r.Status);
        Assert.Equal("PC-PERITO-01", r.Hostname);
        Assert.Equal("installed", r.AgentMode);
        Assert.Equal("R58RA3R5AGA", r.Device!.Serial);
        Assert.Equal(30, r.Device.SdkInt);
        Assert.Equal("android", r.DeviceDetails!.Platform);
        Assert.True(r.DeviceDetails.Fields!.ContainsKey("serial_number"));
        Assert.Equal("masked", r.DeviceDetails.Fields["wifi_ssid"].Reason);
        Assert.NotNull(r.DeviceDetails.ClockStart);
        Assert.NotNull(r.DeviceDetails.ClockClose);
        Assert.Equal(2, r.Categories!.Count);
        Assert.Equal("contacts", r.Categories[0].Key);
        Assert.Equal(22, r.Categories[0].Count);
        Assert.Equal(812, r.Categories[1].Files);
        Assert.Equal(19922944, r.Totals!.Bytes);
        Assert.Equal(830, r.ManifestEntries);
        Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", r.ManifestSha256);
        Assert.Single(r.DeviceModifications!);
        Assert.Equal("connector_installed", r.DeviceModifications![0].Kind);
        Assert.NotNull(r.PcServerProbe);
        Assert.Null(r.Interruption);
    }

    [Fact]
    public void Record_AplicadoPorLasReglas_ProduceExtraccionValida()
    {
        var req = Load();
        var id = Guid.NewGuid().ToString("D");
        var officer = new User { Dni = "30111222", Name = "Ana Pérez" };
        // Cerca del sondeo del fixture (17:30:00Z) para que pc_server_probe no quede "stale" (ProbeMaxAge 5 min).
        var now = new DateTime(2026, 10, 25, 17, 30, 1, DateTimeKind.Utc);

        var result = ExtractionRules.Apply(null, id, req, officer, now);
        Assert.Equal(ExtractionRuleError.None, result.Error);
        var e = result.Value!;
        Assert.Equal(ExtractionStatuses.CompletedWithErrors, e.Status);
        Assert.Equal(2, e.Categories.Count);
        Assert.Equal(812, e.Totals.Files);
        Assert.NotNull(e.DeviceDetails);
        Assert.Single(e.DeviceModifications);
        // pc_server_probe válido → se computó la diferencia PC ↔ servidor.
        Assert.NotNull(e.PcMinusServerMs);
    }
}
