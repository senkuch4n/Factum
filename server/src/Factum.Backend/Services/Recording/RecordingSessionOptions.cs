using System.Globalization;

namespace Factum.Backend.Services.Recording;

/// <summary>Modos de la grabación de la sesión en la instalación (grabacion-sesion-completa §6.6, D1).</summary>
public static class RecordingSessionModes
{
    public const string Disabled = "disabled";
    public const string OptionalOff = "optional_off";
    public const string OptionalOn = "optional_on";
    public const string Required = "required";

    public static bool IsValid(string? mode) => mode is Disabled or OptionalOff or OptionalOn or Required;
}

/// <summary>
/// Config de instalación <c>Recording:*</c> (§6.6): <c>Recording:SessionMode</c> (default <c>optional_off</c>) y
/// <c>Recording:SegmentMinutes</c> (5–120, default 30). Un valor inválido no impide arrancar: queda el default y un
/// warning en el log de arranque (como <c>ReportSettings</c>). Variables: <c>FACTUM_GRABACION_SESION_MODO</c> y
/// <c>FACTUM_GRABACION_SESION_TRAMO_MINUTOS</c> (compose).
/// </summary>
public sealed class RecordingSessionOptions
{
    public const string DefaultMode = RecordingSessionModes.OptionalOff;
    public const int DefaultSegmentMinutes = 30;
    public const int MinSegmentMinutes = 5;
    public const int MaxSegmentMinutes = 120;

    public string Mode { get; }
    public int SegmentMinutes { get; }
    public IReadOnlyList<string> Warnings { get; }

    public RecordingSessionOptions(string mode = DefaultMode, int segmentMinutes = DefaultSegmentMinutes,
        IReadOnlyList<string>? warnings = null)
    {
        Mode = mode;
        SegmentMinutes = segmentMinutes;
        Warnings = warnings ?? [];
    }

    public static RecordingSessionOptions Default { get; } = new();

    /// <summary>Lee y valida la sección <c>Recording</c>. Nunca lanza.</summary>
    public static RecordingSessionOptions Resolve(IConfiguration config) =>
        Resolve(config["Recording:SessionMode"], config["Recording:SegmentMinutes"]);

    /// <summary>Puro (testeable).</summary>
    public static RecordingSessionOptions Resolve(string? rawMode, string? rawMinutes)
    {
        var warnings = new List<string>();
        var mode = DefaultMode;
        if (!string.IsNullOrWhiteSpace(rawMode))
        {
            var m = rawMode.Trim().ToLowerInvariant();
            if (RecordingSessionModes.IsValid(m)) mode = m;
            else
                warnings.Add($"Recording:SessionMode inválido ('{rawMode}'); se usa {DefaultMode}. " +
                             "Valores: disabled, optional_off, optional_on, required.");
        }

        var minutes = DefaultSegmentMinutes;
        if (!string.IsNullOrWhiteSpace(rawMinutes))
        {
            if (int.TryParse(rawMinutes.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) &&
                n is >= MinSegmentMinutes and <= MaxSegmentMinutes)
                minutes = n;
            else
                warnings.Add($"Recording:SegmentMinutes inválido ('{rawMinutes}'); se usa {DefaultSegmentMinutes}. " +
                             $"Rango: {MinSegmentMinutes}–{MaxSegmentMinutes}.");
        }
        return new RecordingSessionOptions(mode, minutes, warnings);
    }
}
