using System.Text.RegularExpressions;
using Factum.Agent.Common;
using Factum.Agent.Models;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Services;

/// <summary>Estado de la pantalla de un Android antes de capturar (pulido-post-qa-final D9).</summary>
public enum ScreenState { On, Off, Locked, Unknown }

/// <summary><c>Reason</c> solo con <see cref="ScreenState.Unknown"/>: <c>timeout</c> | <c>adb_error</c> | <c>no_signals</c>.</summary>
public sealed record ScreenProbeResult(ScreenState State, string? Reason);

/// <summary>
/// Consulta de solo lectura (<c>dumpsys</c> filtrado en el celular) para saber si la pantalla está apagada o
/// bloqueada antes de una captura de pantalla o de iniciar una grabación de Android (pulido-post-qa-final §9.1).
/// </summary>
public interface IAndroidScreenProbe
{
    Task<ScreenProbeResult> GetStateAsync(string serial, CancellationToken ct);
}

public sealed partial class AndroidScreenProbe : IAndroidScreenProbe
{
    // Misma resolución que AdbService (portátil, PATH o Homebrew); se duplica el Lazy para no tocar AdbService.
    private static readonly Lazy<string> AdbPath =
        new(() => ToolResolver.Find(AgentTools.Adb)?.Path ?? "adb");

    /// <summary>
    /// Se filtra con grep en el celular: no viaja la salida completa de dumpsys (D-T2). Nunca se loguea.
    /// </summary>
    internal const string Script =
        "dumpsys power | grep -E 'mWakefulness=|getWakefulnessLocked|Display Power: state='; echo __FX_SEP__; " +
        "dumpsys window policy | grep -E 'showing=|showingAndNotOccluded=|occluded=|mIsShowing=|mShowingLockscreen=|isStatusBarKeyguard=|isKeyguardShowing=|mKeyguardOccluded='";

    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan MockDelay = TimeSpan.FromMilliseconds(100);

    private readonly bool _mock;
    private readonly string? _mockState;
    private readonly ILogger<AndroidScreenProbe> _log;
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<ProcessResult>> _run;

    public AndroidScreenProbe(IOptions<AgentOptions> opts, ILogger<AndroidScreenProbe> log)
        : this(opts, log, (args, ct) => ProcessRunner.RunArgumentListAsync(AdbPath.Value, args, ct, timeout: Timeout))
    {
    }

    /// <summary>Para los tests: el ejecutor de adb inyectado.</summary>
    internal AndroidScreenProbe(IOptions<AgentOptions> opts, ILogger<AndroidScreenProbe> log,
        Func<IReadOnlyList<string>, CancellationToken, Task<ProcessResult>> run)
    {
        _mock = opts.Value.Mock;
        _mockState = opts.Value.MockAndroidScreenState?.Trim().ToLowerInvariant();
        _log = log;
        _run = run;
    }

    public async Task<ScreenProbeResult> GetStateAsync(string serial, CancellationToken ct)
    {
        var result = _mock ? await MockAsync(ct) : await ProbeAsync(serial, ct);
        // D-T4: sin serial, sin salida cruda, sin datos del caso.
        switch (result.State)
        {
            case ScreenState.Unknown:
                _log.LogInformation("Chequeo de pantalla no concluyente ({Reason}); se captura sin chequeo",
                    result.Reason);
                break;
            case ScreenState.Off:
            case ScreenState.Locked:
                _log.LogInformation("Captura frenada: pantalla {State}", ToWire(result.State));
                break;
        }
        return result;
    }

    private async Task<ScreenProbeResult> MockAsync(CancellationToken ct)
    {
        await Task.Delay(MockDelay, ct);
        return _mockState switch
        {
            "off" => new(ScreenState.Off, null),
            "locked" => new(ScreenState.Locked, null),
            "unknown" => new(ScreenState.Unknown, "no_signals"),
            _ => new(ScreenState.On, null),
        };
    }

    private async Task<ScreenProbeResult> ProbeAsync(string serial, CancellationToken ct)
    {
        var r = await _run(["-s", serial, "shell", Script], ct);
        if (r.ExitCode == ProcessRunner.TimeoutExitCode) return new(ScreenState.Unknown, "timeout");
        // grep sin coincidencias en el último comando devuelve 1: con salida, se parsea igual.
        if (r.ExitCode == -1 || (r.ExitCode != 0 && string.IsNullOrWhiteSpace(r.Stdout)))
            return new(ScreenState.Unknown, "adb_error");
        var state = Parse(r.Stdout);
        return state == ScreenState.Unknown ? new(state, "no_signals") : new(state, null);
    }

    /// <summary>
    /// Puro (§9.1). Orden: apagada antes que bloqueada (con la pantalla apagada el keyguard da señales
    /// contradictorias). Una pantalla de bloqueo ocluida cuenta como encendida (D-T3). Las claves se buscan
    /// como token (precedidas por algo que no sea letra) y respetando mayúsculas: <c>showing=</c> no matchea
    /// <c>mAodShowing=</c> ni <c>mIsShowing=</c>.
    /// </summary>
    internal static ScreenState Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return ScreenState.Unknown;

        var display = Match(DisplayRegex(), output);
        var wake = Match(WakefulnessRegex(), output) ?? Match(WakefulnessLockedRegex(), output);

        var displayOff = display is "OFF" or "DOZE" or "DOZE_SUSPEND";
        var wakeOff = wake is "Asleep" or "Dozing";
        if (displayOff || wakeOff) return ScreenState.Off;

        var awake = wake == "Awake" || display == "ON";
        if (!awake) return ScreenState.Unknown;

        // 1) KeyguardServiceDelegate: showing= + occluded=.
        var showing = Bool(Key("showing"), output);
        if (showing is not null)
        {
            var occluded = Bool(Key("occluded"), output);
            return showing == true && occluded != true ? ScreenState.Locked : ScreenState.On;
        }

        // 2) Señales alternativas según versión/fabricante.
        bool? any = null;
        foreach (var k in AltKeyguardKeys)
        {
            var v = Bool(Key(k), output);
            if (v is null) continue;
            any = (any ?? false) || v.Value;
        }
        if (any is null) return ScreenState.Unknown;
        if (any == false) return ScreenState.On;
        var keyguardOccluded = Bool(Key("mKeyguardOccluded"), output);
        return keyguardOccluded == true ? ScreenState.On : ScreenState.Locked;
    }

    private static readonly string[] AltKeyguardKeys =
        ["isKeyguardShowing", "mShowingLockscreen", "isStatusBarKeyguard", "mIsShowing"];

    /// <summary>
    /// 409 de screenshot / record/start (§6.2) con <c>screen_state</c> en el cuerpo, o null si se puede capturar.
    /// <paramref name="verb"/>: "capturar" | "grabar".
    /// </summary>
    internal static AgentHttpException? ScreenBlock(ScreenState state, string verb)
    {
        if (state is not (ScreenState.Off or ScreenState.Locked)) return null;
        var code = state == ScreenState.Off ? AgentErrorCodes.ScreenOff : AgentErrorCodes.ScreenLocked;
        return new AgentHttpException(StatusCodes.Status409Conflict, code,
            $"La pantalla del celular está apagada o bloqueada. Encendela y desbloqueala para {verb}.",
            new Dictionary<string, object?> { ["screen_state"] = ToWire(state) });
    }

    internal static string ToWire(ScreenState s) => s switch
    {
        ScreenState.On => "on",
        ScreenState.Off => "off",
        ScreenState.Locked => "locked",
        _ => "unknown",
    };

    private static string? Match(Regex re, string text)
    {
        var m = re.Match(text);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static Regex Key(string key) =>
        new($@"(?<![A-Za-z]){Regex.Escape(key)}=(\w+)", RegexOptions.CultureInvariant);

    private static bool? Bool(Regex re, string text) => Match(re, text) switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };

    [GeneratedRegex(@"Display Power: state=(\w+)", RegexOptions.CultureInvariant)]
    private static partial Regex DisplayRegex();

    [GeneratedRegex(@"(?<![A-Za-z])mWakefulness=(\w+)", RegexOptions.CultureInvariant)]
    private static partial Regex WakefulnessRegex();

    [GeneratedRegex(@"(?<![A-Za-z])getWakefulnessLocked\(\)=(\w+)", RegexOptions.CultureInvariant)]
    private static partial Regex WakefulnessLockedRegex();
}
