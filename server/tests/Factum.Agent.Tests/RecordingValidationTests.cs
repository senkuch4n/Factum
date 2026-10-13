using Factum.Agent.Common;
using Factum.Agent.Models;
using Factum.Agent.Services;

namespace Factum.Agent.Tests;

/// <summary>Validación de la grabación de Android (grabacion-android-robusta §5.5, Duda 3 A).</summary>
public sealed class RecordingValidationTests
{
    private static readonly RecordingOptions O = new();
    private static FfmpegProbeResult Probe(double? d, bool video = true) => new(d, video);

    [Theory]
    [InlineData(60, 3)]
    [InlineData(686, 13.72)]
    [InlineData(0, 3)]
    [InlineData(-5, 3)]
    public void Tolerance_EsMaxEntreMinimoYPorcentaje(double recorded, double expected) =>
        Assert.Equal(expected, RecordingValidation.Tolerance(recorded, O), 3);

    [Fact]
    public void Tolerance_ConfigPropia()
    {
        var o = new RecordingOptions { ToleranceMinSeconds = 5, TolerancePercent = 10 };
        Assert.Equal(5, RecordingValidation.Tolerance(10, o), 3);
        Assert.Equal(60, RecordingValidation.Tolerance(600, o), 3);
    }

    [Fact]
    public void Evaluate_Size0_Empty() =>
        Assert.Equal(RecordingValidationStatus.Empty, RecordingValidation.Evaluate(0, Probe(10), 10, O).Status);

    [Fact]
    public void Evaluate_SinFfmpeg_OkSinChequear()
    {
        var (status, duration, chk) = RecordingValidation.Evaluate(100, null, 10, O);
        Assert.Equal(RecordingValidationStatus.Ok, status);
        Assert.Null(duration);
        Assert.False(chk);
    }

    [Fact]
    public void Evaluate_SinDuracion_Unreadable() =>
        Assert.Equal(RecordingValidationStatus.Unreadable, RecordingValidation.Evaluate(100, Probe(null), 10, O).Status);

    [Fact]
    public void Evaluate_SinVideo_Unreadable() =>
        Assert.Equal(RecordingValidationStatus.Unreadable,
            RecordingValidation.Evaluate(100, Probe(10, video: false), 10, O).Status);

    [Theory]
    [InlineData(550.5, 686, RecordingValidationStatus.Incomplete)] // el caso del QA
    [InlineData(683, 686, RecordingValidationStatus.Ok)]
    [InlineData(700, 686, RecordingValidationStatus.Ok)]           // más largo que lo grabado: ok
    [InlineData(1, -3, RecordingValidationStatus.Ok)]              // grabado negativo → 0
    [InlineData(26.9, 30, RecordingValidationStatus.Incomplete)]
    [InlineData(27, 30, RecordingValidationStatus.Ok)]
    public void Evaluate_ComparaConLaTolerancia(double duration, double recorded, string expected)
    {
        var (status, d, chk) = RecordingValidation.Evaluate(1000, Probe(duration), recorded, O);
        Assert.Equal(expected, status);
        Assert.Equal(duration, d);
        Assert.True(chk);
    }

    [Fact]
    public void Dto_RedondeaAUnDecimal()
    {
        var t = DateTimeOffset.UnixEpoch;
        var dto = RecordingValidationDto.Create("ok", 686.04, 550.46, true, false, t, t);
        Assert.Equal(686.0, dto.RecordedSeconds);
        Assert.Equal(550.5, dto.DurationSeconds);
        Assert.Null(RecordingValidationDto.Create("ok", 1, null, false, false, t, t).DurationSeconds);
    }
}
