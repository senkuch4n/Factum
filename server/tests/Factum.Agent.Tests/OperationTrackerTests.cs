using Factum.Agent.Controllers;
using Factum.Agent.Services;

namespace Factum.Agent.Tests;

/// <summary>Operaciones en curso + modo mantenimiento (tatana-instalador-autoupdate D5, D-T10, §5.5).</summary>
public sealed class OperationTrackerTests
{
    private sealed class FakeTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 7, 15, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeSource : IOperationSource
    {
        public List<AgentOperation> Ops { get; } = [];
        public IEnumerable<AgentOperation> ActiveOperations => Ops;
    }

    private sealed class BrokenSource : IOperationSource
    {
        public IEnumerable<AgentOperation> ActiveOperations => throw new InvalidOperationException("roto");
    }

    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(120);

    private static (OperationTracker Tracker, FakeTime Time, FakeSource Source) Create()
    {
        var time = new FakeTime();
        var source = new FakeSource();
        return (new OperationTracker([source], time), time, source);
    }

    [Fact]
    public void Libre_EntraYRechazaRequestsNuevas()
    {
        var (t, time, _) = Create();
        Assert.False(t.IsBusy);
        Assert.True(t.TryEnterMaintenance(Ttl, out var expires, out var busy));
        Assert.Empty(busy);
        Assert.Equal(time.Now + Ttl, expires);
        Assert.True(t.InMaintenance);

        Assert.False(t.TryEnterRequest("POST", "/cases/abc/evidence/upload", out var token));
        Assert.Null(token);
        Assert.Empty(t.Snapshot());
    }

    [Fact]
    public void ConRequestEnVuelo_NoEntra()
    {
        var (t, _, _) = Create();
        Assert.True(t.TryEnterRequest("POST", "/cases/0123456789abcdef01234567/evidence/upload", out var token));
        Assert.NotNull(token);
        Assert.True(t.IsBusy);

        Assert.False(t.TryEnterMaintenance(Ttl, out _, out var busy));
        var op = Assert.Single(busy);
        Assert.Equal("request", op.Kind);
        Assert.Equal("POST /cases", op.Detail); // sin id del caso
        Assert.False(t.InMaintenance);
    }

    [Fact]
    public void ConFuenteActiva_NoEntra()
    {
        var (t, time, source) = Create();
        source.Ops.Add(new AgentOperation("recording_android", time.Now.AddMinutes(-3)));
        Assert.True(t.IsBusy);
        Assert.False(t.TryEnterMaintenance(Ttl, out _, out var busy));
        Assert.Equal("recording_android", Assert.Single(busy).Kind);

        source.Ops.Clear();
        Assert.True(t.TryEnterMaintenance(Ttl, out _, out _));
    }

    [Fact]
    public void DisponerElToken_BajaElContador()
    {
        var (t, _, _) = Create();
        Assert.True(t.TryEnterRequest("DELETE", "/cases/x/evidence/y", out var a));
        Assert.True(t.TryEnterRequest("PUT", "/files/z", out var b));
        Assert.Equal(2, t.Snapshot().Count);
        a!.Dispose();
        a.Dispose(); // idempotente: no baja dos veces
        Assert.Single(t.Snapshot());
        b!.Dispose();
        Assert.Empty(t.Snapshot());
        Assert.True(t.TryEnterMaintenance(Ttl, out _, out _));
    }

    [Fact]
    public void TtlVencido_VuelveAAceptarRequests()
    {
        var (t, time, _) = Create();
        Assert.True(t.TryEnterMaintenance(Ttl, out _, out _));
        time.Now += Ttl - TimeSpan.FromSeconds(1);
        Assert.True(t.InMaintenance);
        Assert.False(t.TryEnterRequest("POST", "/screenshot", out _));
        time.Now += TimeSpan.FromSeconds(1);
        Assert.False(t.InMaintenance);
        Assert.True(t.TryEnterRequest("POST", "/screenshot", out var token));
        token!.Dispose();
    }

    [Fact]
    public void ExitMaintenance_VuelveAAceptar()
    {
        var (t, _, _) = Create();
        Assert.True(t.TryEnterMaintenance(Ttl, out _, out _));
        t.ExitMaintenance();
        Assert.False(t.InMaintenance);
        Assert.True(t.TryEnterRequest("POST", "/recording/start", out var token));
        token!.Dispose();
        t.ExitMaintenance(); // idempotente
    }

    [Fact]
    public void FuenteRota_NoTrabaElEstado()
    {
        var time = new FakeTime();
        var t = new OperationTracker([new BrokenSource()], time);
        Assert.Empty(t.Snapshot());
        Assert.True(t.TryEnterMaintenance(Ttl, out _, out _));
    }

    [Fact]
    public void SnapshotOrdenadoPorInicio()
    {
        var (t, time, source) = Create();
        source.Ops.Add(new AgentOperation("recording_ios", time.Now.AddMinutes(-1)));
        source.Ops.Add(new AgentOperation("video_postprocess", time.Now.AddMinutes(-5)));
        Assert.True(t.TryEnterRequest("POST", "/cases/x/zip", out var token));
        Assert.Equal(["video_postprocess", "recording_ios", "request"], t.Snapshot().Select(o => o.Kind));
        token!.Dispose();
    }

    [Theory]
    [InlineData("POST", "/cases/1/zip", true)]
    [InlineData("put", "/x", true)]
    [InlineData("PATCH", "/x", true)]
    [InlineData("DELETE", "/cases/1/evidence/a.jpg", true)]
    [InlineData("GET", "/health", false)]
    [InlineData("GET", "/ws", false)]
    [InlineData("OPTIONS", "/cases", false)]
    [InlineData("POST", "/agent/maintenance", false)]
    [InlineData("DELETE", "/agent/maintenance/", false)]
    [InlineData("POST", "/Agent/Maintenance", false)]
    public void IsTracked(string method, string path, bool expected) =>
        Assert.Equal(expected, OperationTracker.IsTracked(method, path));

    [Theory]
    [InlineData("post", "/cases/0123/evidence/upload", "POST /cases")]
    [InlineData("POST", "/screenshot", "POST /screenshot")]
    [InlineData("DELETE", "/", "DELETE /")]
    public void DescribeRequest(string method, string path, string expected) =>
        Assert.Equal(expected, OperationTracker.DescribeRequest(method, path));

    // ── AgentStateController (puro) ──────────────────────────────────────────

    [Theory]
    [InlineData(null, 120)]
    [InlineData(1, 10)]
    [InlineData(120, 120)]
    [InlineData(9999, 600)]
    public void ClampTtl(int? requested, int expected) =>
        Assert.Equal(expected, AgentStateController.ClampTtl(requested));

    [Theory]
    [InlineData("{\"ttl_seconds\": 30}", 30)]
    [InlineData("{}", null)]
    [InlineData("", null)]
    [InlineData("roto", null)]
    public void ParseTtl(string body, int? expected) =>
        Assert.Equal(expected, AgentStateController.ParseTtl(body));
}
