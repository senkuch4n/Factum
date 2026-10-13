using Factum.Agent.Models;

namespace Factum.Agent.Tests;

/// <summary><c>Agent:Recording:*</c>: un valor inválido vuelve al default de ese campo (grabacion-android-robusta §5.1).</summary>
public sealed class RecordingOptionsTests
{
    [Fact]
    public void Defaults_SonValidos()
    {
        var o = new RecordingOptions().Normalize(null);
        Assert.Equal(20, o.StartTimeoutSeconds);
        Assert.True(o.RequireStartMarker);
        Assert.Equal(30, o.StopTimeoutSeconds);
        Assert.Equal(3, o.SigtermAfterSeconds);
        Assert.True(o.StayAwake);
        Assert.Equal("no_window", o.WindowMode);
        Assert.Equal(3, o.ToleranceMinSeconds);
        Assert.Equal(2, o.TolerancePercent);
    }

    [Fact]
    public void Normalize_CampoPorCampo()
    {
        var o = new RecordingOptions
        {
            StopTimeoutSeconds = 0, TolerancePercent = 80, WindowMode = "x", StartTimeoutSeconds = -1,
            ToleranceMinSeconds = 0, StayAwake = false,
        }.Normalize(null);
        Assert.Equal(30, o.StopTimeoutSeconds);
        Assert.Equal(2, o.TolerancePercent);
        Assert.Equal("no_window", o.WindowMode);
        Assert.Equal(20, o.StartTimeoutSeconds);
        Assert.Equal(3, o.ToleranceMinSeconds);
        Assert.False(o.StayAwake); // válido: no se toca
    }

    [Fact]
    public void Normalize_SigtermMayorOIgualQueStop_VuelveAlDefault()
    {
        Assert.Equal(3, new RecordingOptions { SigtermAfterSeconds = 40, StopTimeoutSeconds = 30 }.Normalize(null).SigtermAfterSeconds);
        Assert.Equal(3, new RecordingOptions { SigtermAfterSeconds = -1 }.Normalize(null).SigtermAfterSeconds);
        Assert.Equal(0, new RecordingOptions { SigtermAfterSeconds = 0 }.Normalize(null).SigtermAfterSeconds);
        // Con un stop muy corto el default no entra: sin escalado.
        Assert.Equal(0, new RecordingOptions { SigtermAfterSeconds = 5, StopTimeoutSeconds = 2 }.Normalize(null).SigtermAfterSeconds);
    }

    [Fact]
    public void Normalize_WindowMode_AceptaMayusculas()
    {
        Assert.Equal("no_video_playback", new RecordingOptions { WindowMode = " No_Video_Playback " }.Normalize(null).WindowMode);
    }
}
