using Factum.Agent.Common;
using Factum.Agent.Models;
using Factum.Agent.Services;
using Factum.Agent.WebSockets;
using Microsoft.Extensions.Logging.Abstractions;

namespace Factum.Agent.Tests;

/// <summary>tatana-linux D-T20: <c>Agent:VideoPostprocess:MinProcessorCount</c>.</summary>
public sealed class VideoPostprocessOptionsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "factum-ppo-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Theory]
    [InlineData(null, HostOs.Linux, 4)]
    [InlineData(null, HostOs.Windows, 0)]
    [InlineData(null, HostOs.MacOS, 0)]
    [InlineData(2, HostOs.Linux, 2)]
    [InlineData(8, HostOs.Windows, 8)]
    [InlineData(0, HostOs.Linux, 0)]
    public void Minimo_efectivo_por_SO(int? configured, HostOs os, int expected) =>
        Assert.Equal(expected, new VideoPostprocessOptions { MinProcessorCount = configured }.EffectiveMinProcessorCount(os));

    [Fact]
    public void Negativo_vuelve_a_null()
    {
        var o = new VideoPostprocessOptions { MinProcessorCount = -1 }.Normalize(NullLogger.Instance);
        Assert.Null(o.MinProcessorCount);
        Assert.Equal(3, new VideoPostprocessOptions { MinProcessorCount = 3 }.Normalize(null).MinProcessorCount);
    }

    [Theory]
    [InlineData(2, 4, false)]
    [InlineData(4, 4, true)]
    [InlineData(1, 0, true)]
    public void Nucleos_suficientes(int count, int min, bool expected) =>
        Assert.Equal(expected, VideoPostprocessService.HasEnoughProcessors(count, min));

    [Fact]
    public void PC_chica_en_Linux_no_genera_variantes()
    {
        Directory.CreateDirectory(_root);
        var video = Path.Combine(_root, "grabacion_ios_x.mp4");
        File.WriteAllBytes(video, [0, 1, 2]);
        using var svc = new VideoPostprocessService(
            new AgentOptions { DataDirectory = _root, MinFreeBytes = 0, VideoPostprocess = new VideoPostprocessOptions() },
            new NullSink(), null, null, null, ffmpegPath: "/no/existe/ffmpeg", rifePath: null,
            processorCount: 2, os: HostOs.Linux);
        Assert.Empty(svc.Start(video, TimeSpan.FromSeconds(1)));
    }

    private sealed class NullSink : IAgentEventSink
    {
        public Task BroadcastAsync(AgentEvent evt, CancellationToken ct = default) => Task.CompletedTask;
    }
}
