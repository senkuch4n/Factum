using Factum.Agent.Common;
using Factum.Agent.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Services.Session;

/// <summary>
/// "Pantalla siempre encendida" durante la sesión (grabacion-sesion-completa §5.5, DU2=A). Con el corte solapado, cada
/// scrcpy restaura <c>stay_on_while_plugged_in</c> al cerrarse aunque el siguiente siga grabando (§4.2.4). Tatana
/// guarda el valor original, reafirma el mismo 7 que pone scrcpy después de cada corte y restaura el original al
/// terminar. Nunca corta la sesión: si adb falla, loguea y sigue. Con <c>Agent:Recording:StayAwake=false</c> o en
/// mock, no hace nada.
/// </summary>
public sealed class StayAwakeKeeper
{
    /// <summary>El valor que pone scrcpy con <c>--stay-awake</c>: AC | USB | WIRELESS.</summary>
    public const string ScrcpyValue = "7";
    private const string Setting = "stay_on_while_plugged_in";
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static readonly Lazy<string> AdbPath = new(() => ToolResolver.Find(AgentTools.Adb)?.Path ?? "adb");

    private readonly bool _enabled;
    private readonly ILogger _log;
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<ProcessResult>> _run;

    public StayAwakeKeeper(IOptions<AgentOptions> opts, ILogger<StayAwakeKeeper> log)
        : this(!opts.Value.Mock && (opts.Value.Recording?.StayAwake ?? true), log,
            (args, ct) => ProcessRunner.RunArgumentListAsync(AdbPath.Value, args, ct, timeout: Timeout))
    {
    }

    /// <summary>Para los tests: el ejecutor de adb inyectado.</summary>
    internal StayAwakeKeeper(bool enabled, ILogger? log,
        Func<IReadOnlyList<string>, CancellationToken, Task<ProcessResult>> run)
    {
        _enabled = enabled;
        _log = log ?? NullLogger.Instance;
        _run = run;
    }

    public bool Enabled => _enabled;

    /// <summary>Valor actual (trim) o null si adb falló.</summary>
    public async Task<string?> ReadAsync(string serial)
    {
        try
        {
            var r = await _run(["-s", serial, "shell", $"settings get global {Setting}"], CancellationToken.None);
            if (!r.Success) return null;
            var v = r.Stdout.Trim();
            return v.Length == 0 ? null : v;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se pudo leer «pantalla siempre encendida»");
            return null;
        }
    }

    private async Task<bool> WriteAsync(string serial, string value)
    {
        try
        {
            var r = await _run(["-s", serial, "shell", $"settings put global {Setting} {value}"], CancellationToken.None);
            if (!r.Success) _log.LogWarning("No se pudo escribir «pantalla siempre encendida» (exit {Code})", r.ExitCode);
            return r.Success;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se pudo escribir «pantalla siempre encendida»");
            return false;
        }
    }

    /// <summary>
    /// Al empezar o reanudar: si el diario todavía no tiene el valor original para este serial, lo lee y lo guarda.
    /// Devuelve el estado a guardar en el diario (el mismo objeto u otro nuevo).
    /// </summary>
    public async Task<StayAwakeState?> BeginAsync(StayAwakeState? state, string serial)
    {
        if (!_enabled) return state;
        if (state is not null && state.Serial == serial && state.Original is not null) return state;
        if (state is not null && state.Serial != serial && (state.Owned || state.RestorePending))
            _log.LogWarning("La sesión cambió de celular con una restauración de «pantalla siempre encendida» pendiente en el anterior");
        var original = await ReadAsync(serial);
        return new StayAwakeState { Serial = serial, Original = original, Owned = false, RestorePending = false };
    }

    /// <summary>Después de cerrar cada tramo saliente: si el valor no es 7, pone 7 y marca <c>owned</c>.</summary>
    public async Task ReassertAsync(StayAwakeState? state, string serial)
    {
        if (!_enabled || state is null || state.Serial != serial) return;
        var current = await ReadAsync(serial);
        if (current is null || current == ScrcpyValue) return;
        if (await WriteAsync(serial, ScrcpyValue))
        {
            state.Owned = true;
            _log.LogInformation("«Pantalla siempre encendida» reafirmada tras el corte de la sesión");
        }
    }

    /// <summary>
    /// Al terminar la sesión (después del último scrcpy): si Tatana la escribió y el valor sigue en 7, vuelve al
    /// original. Si el valor no es 7, alguien lo cambió y no se toca. Sin celular conectado, queda
    /// <c>restore_pending</c>.
    /// </summary>
    public async Task RestoreAsync(StayAwakeState? state, bool connected)
    {
        if (!_enabled || state is null || (!state.Owned && !state.RestorePending)) return;
        if (!connected)
        {
            state.RestorePending = true;
            return;
        }
        var current = await ReadAsync(state.Serial);
        if (current is null)
        {
            state.RestorePending = true;
            return;
        }
        if (current == ScrcpyValue && state.Original is { } original && original != ScrcpyValue)
        {
            if (!await WriteAsync(state.Serial, original))
            {
                state.RestorePending = true;
                return;
            }
            _log.LogInformation("«Pantalla siempre encendida» restaurada a su valor original");
        }
        state.Owned = false;
        state.RestorePending = false;
    }
}
