using Factum.Agent.Common;

namespace Factum.Agent.Tests;

/// <summary>Marcador de inicio de scrcpy (grabacion-android-robusta §5.6, DA3).</summary>
public sealed class ScrcpyOutputTests
{
    [Theory]
    [InlineData("[server] INFO: Device: [samsung] samsung SM-M127F (Android 11)", true)]
    [InlineData("  [server] INFO: Device: [samsung] samsung SM-M127F (Android 11)\r", true)]
    [InlineData("INFO: Recording started to matroska file: /x.mkv", true)]
    [InlineData("[server] DEBUG: Display: using SurfaceControl API", true)]
    [InlineData("INFO: ADB device found:", false)]
    [InlineData("[server] INFO: Device", false)]
    [InlineData("INFO: Recording complete to matroska file: /x.mkv", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsStartMarker(string? line, bool expected) => Assert.Equal(expected, ScrcpyOutput.IsStartMarker(line));

    [Theory]
    [InlineData("DEBUG: Demuxer started", true)]
    [InlineData("[server] DEBUG: Display: using SurfaceControl API", true)]
    [InlineData("ERROR: Could not open video stream", false)]
    [InlineData("[server] INFO: Device: x", false)]
    public void IsDebugLine(string line, bool expected) => Assert.Equal(expected, ScrcpyOutput.IsDebugLine(line));
}
