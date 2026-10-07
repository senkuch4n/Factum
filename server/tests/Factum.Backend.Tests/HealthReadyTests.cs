using System.Diagnostics;
using System.Text.Json;
using Factum.Backend.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Factum.Backend.Tests;

/// <summary>GET /health/ready (despliegue-nube DT3, Contrato compartido §5.2).</summary>
public sealed class HealthReadyTests
{
    private static readonly JsonSerializerOptions SnakeCase = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    [Fact]
    public void ToResult_MongoOk_Returns200()
    {
        var (code, body) = HealthReady.ToResult(true);
        Assert.Equal(200, code);
        Assert.Equal("""{"status":"ok","mongo":"ok"}""", JsonSerializer.Serialize(body, SnakeCase));
    }

    [Fact]
    public void ToResult_MongoDown_Returns503()
    {
        var (code, body) = HealthReady.ToResult(false);
        Assert.Equal(503, code);
        Assert.Equal("""{"status":"unavailable","mongo":"down"}""", JsonSerializer.Serialize(body, SnakeCase));
    }

    /// <summary>No toca ninguna base real: el puerto 1 de loopback no tiene un Mongo escuchando.</summary>
    [Fact]
    public async Task Probe_UnreachableMongo_ReturnsFalseQuickly()
    {
        var probe = new MongoHealthProbe(
            Options.Create(new MongoOptions { ConnectionString = "mongodb://127.0.0.1:1", DatabaseName = "factum_probe_test" }),
            NullLogger<MongoHealthProbe>.Instance);

        var sw = Stopwatch.StartNew();
        var ok = await probe.PingAsync(CancellationToken.None);
        sw.Stop();

        Assert.False(ok);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"tardó {sw.Elapsed}");
    }

    [Fact]
    public async Task Probe_RequestCancelled_Throws()
    {
        var probe = new MongoHealthProbe(
            Options.Create(new MongoOptions { ConnectionString = "mongodb://127.0.0.1:1", DatabaseName = "factum_probe_test" }),
            NullLogger<MongoHealthProbe>.Instance);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe.PingAsync(cts.Token));
    }
}
