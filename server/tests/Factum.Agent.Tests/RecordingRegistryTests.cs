using Factum.Agent.Models;
using Factum.Agent.Services;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Tests;

/// <summary>Grabaciones detenidas sin importar (grabacion-android-robusta §5.7, DA10).</summary>
public sealed class RecordingRegistryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "factum-rec-" + Guid.NewGuid().ToString("N"));

    private sealed class FakeTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 13, 15, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    public RecordingRegistryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private (RecordingRegistry Reg, FakeTime Time) Create()
    {
        var time = new FakeTime();
        return (new RecordingRegistry(Options.Create(new AgentOptions { DataDirectory = _root }), time), time);
    }

    private string Touch(string name)
    {
        File.WriteAllBytes(Path.Combine(_root, name), [1]);
        return name;
    }

    private static UnclaimedRecordingDto Entry(string name, DateTimeOffset at, string? caseId = null) =>
        new("android", "S1", name, caseId, at, null);

    [Fact]
    public void SoloListaLoQueExisteEnDisco()
    {
        var (reg, time) = Create();
        reg.AddStopped(Entry(Touch("a.mkv"), time.Now));
        reg.AddStopped(Entry("no_existe.mkv", time.Now));
        Assert.Equal(["a.mkv"], reg.Unclaimed().Select(x => x.Filename));
        File.Delete(Path.Combine(_root, "a.mkv")); // importada (movida al caso)
        Assert.Empty(reg.Unclaimed());
    }

    [Fact]
    public void DescartaLasDeMasDe24h()
    {
        var (reg, time) = Create();
        reg.AddStopped(Entry(Touch("vieja.mkv"), time.Now));
        time.Now += TimeSpan.FromHours(23);
        reg.AddStopped(Entry(Touch("nueva.mkv"), time.Now));
        Assert.Equal(2, reg.Unclaimed().Count);
        time.Now += TimeSpan.FromHours(1);
        Assert.Equal(["nueva.mkv"], reg.Unclaimed().Select(x => x.Filename));
    }

    [Fact]
    public void TopeDe20_LasMasNuevasPrimero()
    {
        var (reg, time) = Create();
        for (var i = 0; i < 25; i++)
            reg.AddStopped(Entry(Touch($"g{i:00}.mkv"), time.Now + TimeSpan.FromSeconds(i)));
        var list = reg.Unclaimed();
        Assert.Equal(RecordingRegistry.MaxEntries, list.Count);
        Assert.Equal("g24.mkv", list[0].Filename);
        Assert.Equal("g05.mkv", list[^1].Filename);
    }

    [Fact]
    public void DedupPorFilename()
    {
        var (reg, time) = Create();
        var name = Touch("a.mkv");
        reg.AddStopped(Entry(name, time.Now, "c1"));
        reg.AddStopped(Entry(name, time.Now + TimeSpan.FromSeconds(1), "c2"));
        var only = Assert.Single(reg.Unclaimed());
        Assert.Equal("c2", only.CaseId);
    }
}
