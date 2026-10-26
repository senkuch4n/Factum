using System.Text.Json;
using System.Text.Json.Serialization;
using Factum.Agent.Models;

namespace Factum.Agent.Tests.Extraction;

/// <summary>
/// extraccion-logica-android §12.2 (D-T2), lado Tatana: el MISMO fixture compartido
/// (<c>server/tests/Fixtures/extraction-record.json</c>) que verifica la API deserializa en
/// <see cref="AgentExtractionRecord"/> con todos los campos. Si el contrato se desincroniza, falla uno de los dos.
/// Usa el naming del agente (SnakeCaseLower + JsonStringEnumConverter), igual que Program.cs de Tatana.
/// </summary>
public class ExtractionRecordContractTests
{
    private static readonly JsonSerializerOptions AgentJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    [Fact]
    public void Record_DeserializaYReserializaSinPerderCampos()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "extraction-record.json");
        var rec = JsonSerializer.Deserialize<AgentExtractionRecord>(File.ReadAllText(path), AgentJson)!;

        Assert.Equal("completed_with_errors", rec.Status);
        Assert.Equal("installed", rec.AgentMode);
        Assert.Equal("R58RA3R5AGA", rec.Device!.Serial);
        Assert.Equal(30, rec.Device.SdkInt);
        Assert.Equal("masked", rec.DeviceDetails!.Fields!["wifi_ssid"].Reason);
        Assert.NotNull(rec.DeviceDetails.ClockStart);
        Assert.NotNull(rec.DeviceDetails.ClockClose);
        Assert.Equal(2, rec.Categories!.Count);
        Assert.Equal(22, rec.Categories[0].Count);
        Assert.Equal(812, rec.Totals!.Files);
        Assert.Equal(830, rec.ManifestEntries);
        Assert.Single(rec.DeviceModifications!);
        Assert.Equal("connector_installed", rec.DeviceModifications![0].Kind);
        Assert.NotNull(rec.PcServerProbe);

        // Reserializa a snake_case: las claves que la API espera están presentes.
        var json = JsonSerializer.Serialize(rec, AgentJson);
        foreach (var key in new[] { "manifest_sha256", "device_sha256_available", "custom_paths_count",
                     "started_at_pc", "finished_at_pc", "device_modifications", "pc_server_probe" })
            Assert.Contains($"\"{key}\"", json);
    }
}
