using Factum.Agent.Common;
using Factum.Agent.Models;

namespace Factum.Agent.Services;

/// <summary>Resultado de la validación de una grabación de Android (grabacion-android-robusta §5.5, DA5).</summary>
public static class RecordingValidationStatus
{
    public const string Ok = "ok", Incomplete = "incomplete", Empty = "empty", Unreadable = "unreadable";

    internal static bool IsKnown(string? s) => s is Ok or Incomplete or Empty or Unreadable;
}

/// <summary>
/// <c>validation</c> del stop, de <c>recording_stopped</c>, de <c>unclaimed_recordings[]</c> y del cuerpo 422
/// (contrato §9). Se serializa en snake_case: <c>status</c>, <c>recorded_seconds</c>, <c>duration_seconds</c>,
/// <c>duration_checked</c>, <c>forced_stop</c>, <c>started_at</c>, <c>stopped_at</c>.
/// </summary>
public sealed record RecordingValidationDto(string Status, double RecordedSeconds, double? DurationSeconds,
    bool DurationChecked, bool ForcedStop, DateTimeOffset StartedAt, DateTimeOffset StoppedAt)
{
    /// <summary>Arma el DTO con los segundos redondeados a 1 decimal.</summary>
    internal static RecordingValidationDto Create(string status, double recordedSeconds, double? durationSeconds,
        bool durationChecked, bool forcedStop, DateTimeOffset startedAt, DateTimeOffset stoppedAt) =>
        new(status, Round1(Math.Max(0, recordedSeconds)), durationSeconds is { } d ? Round1(d) : null,
            durationChecked, forcedStop, startedAt, stoppedAt);

    private static double Round1(double v) => Math.Round(v, 1, MidpointRounding.AwayFromZero);
}

/// <summary>Reglas puras de la validación (Duda 3 A). Un solo lugar, después del cierre y del remux.</summary>
internal static class RecordingValidation
{
    /// <summary>max(<c>ToleranceMinSeconds</c>, grabado × <c>TolerancePercent</c> / 100).</summary>
    internal static double Tolerance(double recordedSeconds, RecordingOptions o) =>
        Math.Max(o.ToleranceMinSeconds, Math.Max(0, recordedSeconds) * o.TolerancePercent / 100);

    /// <summary>
    /// En orden: tamaño ≤ 0 → empty; sin ffmpeg (probe null) → ok sin chequear (DA6); sin duración o sin
    /// video → unreadable; duración ≥ grabado − tolerancia → ok (más largo también es ok); si no, incomplete.
    /// </summary>
    internal static (string Status, double? Duration, bool Checked) Evaluate(long size, FfmpegProbeResult? probe,
        double recordedSeconds, RecordingOptions o)
    {
        if (size <= 0) return (RecordingValidationStatus.Empty, null, false);
        if (probe is null) return (RecordingValidationStatus.Ok, null, false);
        if (probe.DurationSeconds is not { } duration || !probe.HasVideo)
            return (RecordingValidationStatus.Unreadable, probe.DurationSeconds, false);
        var recorded = Math.Max(0, recordedSeconds);
        var status = duration >= recorded - Tolerance(recorded, o)
            ? RecordingValidationStatus.Ok
            : RecordingValidationStatus.Incomplete;
        return (status, duration, true);
    }
}
