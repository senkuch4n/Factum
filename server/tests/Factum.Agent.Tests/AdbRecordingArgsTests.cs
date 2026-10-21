using Factum.Agent.Models;
using Factum.Agent.Services;

namespace Factum.Agent.Tests;

/// <summary>Argumentos de scrcpy y de la mezcla del mic (grabacion-android-robusta §5.4, D7).</summary>
public sealed class AdbRecordingArgsTests
{
    [Fact]
    public void BuildMicMuxArgs_SinShortest_YConMapExplicito()
    {
        var args = AdbService.BuildMicMuxArgs("v.mkv", "a.mka", "t.mkv");
        Assert.DoesNotContain("-shortest", args);
        var joined = string.Join(' ', args);
        Assert.Contains("-map 0:v:0 -map 1:a:0 -c:v copy", joined);
        Assert.Equal("t.mkv", args[^1]);
    }

    [Fact]
    public void BuildScrcpyArgs_PorDefecto_NoWindowYStayAwake()
    {
        var args = AdbService.BuildScrcpyArgs("S1", "/x.mkv", "--no-audio", new RecordingOptions());
        Assert.Contains("--no-window", args);
        Assert.DoesNotContain("--no-video-playback", args);
        Assert.Contains("--stay-awake", args);
        Assert.Contains("--no-audio", args);
        Assert.DoesNotContain(args, a => a.StartsWith("--verbosity", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildScrcpyArgs_ModoAnterior_SinStayAwake()
    {
        var o = new RecordingOptions { WindowMode = "no_video_playback", StayAwake = false };
        var args = AdbService.BuildScrcpyArgs("S1", "/x.mkv", "--audio-source=output", o);
        Assert.Contains("--no-video-playback", args);
        Assert.DoesNotContain("--no-window", args);
        Assert.DoesNotContain("--stay-awake", args);
    }

    [Fact]
    public void BuildScrcpyArgs_PorDefecto_ReproduceAudio()
    {
        var args = AdbService.BuildScrcpyArgs("S1", "/x.mkv", "--audio-source=output", new RecordingOptions());
        Assert.DoesNotContain("--no-audio-playback", args);
    }

    [Theory]
    [InlineData("no_window", "--no-window")]
    [InlineData("no_video_playback", "--no-video-playback")]
    public void BuildScrcpyArgs_SinAudioPlayback_AgregaNoAudioPlayback(string mode, string windowFlag)
    {
        var o = new RecordingOptions { WindowMode = mode, AudioPlayback = false };
        var args = AdbService.BuildScrcpyArgs("S1", "/x.mkv", "--audio-source=output", o);
        Assert.Contains(windowFlag, args);
        Assert.Contains("--audio-source=output", args);
        Assert.Single(args, a => a == "--no-audio-playback");
    }

    [Fact]
    public void BuildScrcpyArgs_SinAudioPlayback_ConNoAudio_NoLoAgrega()
    {
        var o = new RecordingOptions { AudioPlayback = false };
        var args = AdbService.BuildScrcpyArgs("S1", "/x.mkv", "--no-audio", o);
        Assert.DoesNotContain("--no-audio-playback", args);
    }

    [Fact]
    public void UniqueDiscardPath_AgregaSufijo()
    {
        var existing = new HashSet<string> { Path.Combine("d", "a.mkv"), Path.Combine("d", "a_1.mkv") };
        Assert.Equal(Path.Combine("d", "a_2.mkv"), AdbService.UniqueDiscardPath("d", "a.mkv", existing.Contains));
        Assert.Equal(Path.Combine("d", "b.mkv"), AdbService.UniqueDiscardPath("d", "b.mkv", existing.Contains));
        var mic = new HashSet<string> { Path.Combine("d", "a.mkv.mic.mka") };
        Assert.Equal(Path.Combine("d", "a.mkv.mic_1.mka"), AdbService.UniqueDiscardPath("d", "a.mkv.mic.mka", mic.Contains));
    }
}
