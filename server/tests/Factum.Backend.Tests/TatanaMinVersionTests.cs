using System.Text.Json;
using Factum.Backend.Controllers;
using Factum.Backend.DTOs;
using Factum.Backend.Services.Branding;
using Factum.Backend.Services.Support;
using Factum.Backend.Services.Tatana;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Factum.Backend.Tests;

/// <summary>
/// Versión mínima de Tatana (SDD tatana-instalador-autoupdate D6, D-T19, §11): validación de
/// <c>Tatana:MinVersion</c> y <c>tatana_min_version</c> en <c>GET /api/config/public</c> (contrato §13.B).
/// </summary>
public sealed class TatanaMinVersionTests
{
    private static readonly JsonSerializerOptions Snake = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("1.4.0", "1.4.0")]
    [InlineData(" 1.4.0 ", "1.4.0")]
    public void Normalize(string? raw, string? expected) => Assert.Equal(expected, TatanaOptions.Normalize(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("1.4.0")]
    [InlineData("10.20.30")]
    public void Validate_Acepta(string? raw) => Assert.Null(TatanaOptions.Validate(raw));

    [Theory]
    [InlineData("1.4")]
    [InlineData("v1.4.0")]
    [InlineData("1.4.0-beta")]
    [InlineData("1.4.0+abc")]
    [InlineData("latest")]
    public void Validate_Rechaza(string raw) =>
        Assert.Equal("Tatana:MinVersion tiene que ser X.Y.Z o vacío", TatanaOptions.Validate(raw));

    private static JsonElement PublicConfig(string? minVersion)
    {
        var controller = new ConfigController(
            new ReportTestSupport.FakeBranding(new BrandingSnapshot(null, [], null)),
            new SupportSettings(false, new SupportIntegrationOptions(), [], []),
            new ReportTestSupport.FakeSettings(),
            Options.Create(new TatanaOptions { MinVersion = minVersion }));
        var ok = Assert.IsType<OkObjectResult>(controller.Public());
        Assert.IsType<PublicConfigResponse>(ok.Value);
        return JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, Snake)).RootElement;
    }

    [Fact]
    public void PublicConfig_SinMinimo_EsNullYSiempreEsta()
    {
        var root = PublicConfig("");
        Assert.True(root.TryGetProperty("tatana_min_version", out var v));
        Assert.Equal(JsonValueKind.Null, v.ValueKind);
        Assert.Equal(
            ["organization_name", "organization_logo_url", "support_enabled", "encrypt_zip", "tatana_min_version"],
            root.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public void PublicConfig_ConMinimo()
    {
        Assert.Equal("1.4.0", PublicConfig(" 1.4.0 ").GetProperty("tatana_min_version").GetString());
    }
}
