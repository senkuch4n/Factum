using Factum.Agent.Common;

namespace Factum.Agent.Tests;

/// <summary><c>ffmpeg -i</c> → duración y stream de video (grabacion-android-robusta §5.3).</summary>
public sealed class FfmpegProbeTests
{
    private const string ScrcpyMkv = """
        Input #0, matroska,webm, from 'grabacion_20261013_122140.mkv':
          Metadata:
            ENCODER         : Lavf61.7.100
          Duration: 00:00:13.98, start: 0.000000, bitrate: 20 kb/s
          Stream #0:0: Video: h264 (High), yuv420p(tv, bt709, progressive), 496x1080, 60 fps, 60 tbr, 1k tbn (default)
          Stream #0:1: Audio: opus, 48000 Hz, stereo, fltp (default)
        At least one output file must be specified
        """;

    [Fact]
    public void Parse_MkvDeScrcpy()
    {
        var r = FfmpegProbe.Parse(ScrcpyMkv);
        Assert.Equal(13.98, r.DurationSeconds);
        Assert.True(r.HasVideo);
    }

    [Fact]
    public void Parse_SoloAudio_SinVideo()
    {
        var r = FfmpegProbe.Parse("""
              Duration: 00:01:00.00, start: 0.000000, bitrate: 128 kb/s
              Stream #0:0: Audio: aac (LC), 48000 Hz, mono, fltp (default)
            """);
        Assert.Equal(60, r.DurationSeconds);
        Assert.False(r.HasVideo);
    }

    [Fact]
    public void Parse_DurationNA_Null() =>
        Assert.Null(FfmpegProbe.Parse("  Duration: N/A, bitrate: N/A\n  Stream #0:0: Video: h264").DurationSeconds);

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Parse_Vacio(string? stderr)
    {
        var r = FfmpegProbe.Parse(stderr);
        Assert.Null(r.DurationSeconds);
        Assert.False(r.HasVideo);
    }

    [Fact]
    public void ParseDuration_LaDeVideoPostprocessReenvia() =>
        Assert.Equal(FfmpegProbe.ParseDuration(ScrcpyMkv),
            Factum.Agent.Services.VideoPostprocessService.ParseFfmpegDuration(ScrcpyMkv));
}
