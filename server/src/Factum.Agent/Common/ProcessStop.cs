using System.Diagnostics;

namespace Factum.Agent.Common;

// Cierre limpio de un proceso de consola (scrcpy) con fallback a Kill
// (SDD grabacion-android-windows §6.3).
public static class ProcessStop
{
    /// <summary>Primera señal de la grabación de la sesión en macOS/Linux (SIGTERM, QA #80 B-1).</summary>
    public const string SessionSignal = "-15";

    private static readonly TimeSpan HelperTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan AfterFailedSignalWait = TimeSpan.FromSeconds(1);

    // true = cerró limpio con la señal (SIGINT/Ctrl+C o, si se escaló, SIGTERM); false = hubo que matarlo.
    // escalateAfter (grabacion-android-robusta §5.2, solo macOS/Linux): si el proceso sigue vivo ese tiempo
    // después de la SIGINT, se manda SIGTERM. El timeout total se cuenta desde la primera señal. En Windows se
    // ignora. Sin escalateAfter, el comportamiento es el de siempre.
    public static async Task<bool> StopGracefullyAsync(Process p, TimeSpan timeout, ILogger log,
        string name, CancellationToken ct = default, TimeSpan? escalateAfter = null) =>
        (await StopAsync(p, timeout, log, name, ct, escalateAfter)).Clean;

    /// <summary>Igual que <see cref="StopGracefullyAsync"/>, pero informa además si hubo escalado a SIGTERM.</summary>
    /// <param name="unixSignal">
    /// Primera señal en macOS/Linux: <c>"-2"</c> (SIGINT, default) o <c>"-15"</c> (SIGTERM). La grabación de la sesión usa
    /// SIGTERM directo (QA #80 B-1): scrcpy/SDL la trata igual que SIGINT (cierra el archivo y sale 0), pero no queda
    /// ignorada si Tatana heredó SIGINT ignorado de quien lo lanzó.
    /// </param>
    public static async Task<ProcessStopResult> StopAsync(Process p, TimeSpan timeout, ILogger log,
        string name, CancellationToken ct = default, TimeSpan? escalateAfter = null, string unixSignal = "-2")
    {
        if (HasExitedSafe(p)) return new ProcessStopResult(true, false);

        var wait = timeout;
        var escalated = false;
        var signalAt = Stopwatch.StartNew();
        if (OperatingSystem.IsWindows())
        {
            var code = await SendWindowsCtrlCAsync(p.Id, log, ct);
            if (code != 0)
            {
                log.LogWarning("No se pudo enviar Ctrl+C a {Name} (código {Code})", name, code);
                wait = AfterFailedSignalWait;
            }
        }
        else
        {
            await SendSignalAsync(p.Id, unixSignal == "-15" ? "-15" : "-2", ct);
            if (unixSignal != "-15" && escalateAfter is { } esc && esc > TimeSpan.Zero && esc < timeout)
            {
                if (!await WaitForExitAsync(p, esc, ct))
                {
                    log.LogWarning("{Name}: SIGINT sin efecto; se envía SIGTERM", name);
                    await SendSignalAsync(p.Id, "-15", ct);
                    escalated = true;
                }
                wait = timeout - signalAt.Elapsed;
                if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
            }
        }

        if (await WaitForExitAsync(p, wait, ct))
        {
            var exit = SafeExitCode(p);
            log.LogInformation("{Name} detenido limpiamente (exit {Code}{Sig})", name, exit,
                escalated ? ", con SIGTERM" : "");
            return new ProcessStopResult(true, escalated);
        }

        try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* ya terminó */ }
        try { await p.WaitForExitAsync(ct); } catch (InvalidOperationException) { }
        log.LogWarning("{Name} no respondió a la señal; se forzó el cierre", name);
        return new ProcessStopResult(false, escalated);
    }

    // true si el proceso terminó dentro de `wait`. Una cancelación de ct se propaga.
    private static async Task<bool> WaitForExitAsync(Process p, TimeSpan wait, CancellationToken ct)
    {
        if (HasExitedSafe(p)) return true;
        using var timeoutCts = new CancellationTokenSource(wait);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        try
        {
            await p.WaitForExitAsync(linked.Token);
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return HasExitedSafe(p);
        }
    }

    // Lanza `Factum.Agent.exe --ctrl-c <pid>` (o `dotnet Factum.Agent.dll --ctrl-c <pid>` en
    // desarrollo) y devuelve su código de salida; -1 si no arrancó o no terminó a tiempo.
    private static async Task<int> SendWindowsCtrlCAsync(int pid, ILogger log, CancellationToken ct)
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath)) return -1;

        // El dll solo hace falta cuando el host es `dotnet` (desarrollo). No se usa
        // Assembly.Location: en single-file da "" y además dispara el warning IL3000 del publish.
        var entryAssemblyPath = Path.Combine(AppContext.BaseDirectory,
            typeof(ProcessStop).Assembly.GetName().Name + ".dll");
        var (fileName, args) = WindowsConsoleSignal.BuildHelperCommand(processPath, entryAssemblyPath, pid);

        var psi = new ProcessStartInfo
        {
            FileName        = fileName,
            UseShellExecute = false,
            CreateNoWindow  = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        try
        {
            using var helper = Process.Start(psi);
            if (helper is null) return -1;
            using var timeoutCts = new CancellationTokenSource(HelperTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            try
            {
                await helper.WaitForExitAsync(linked.Token);
                return helper.ExitCode;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { helper.Kill(); } catch { }
                return -1;
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            log.LogDebug(ex, "No arrancó el auxiliar de Ctrl+C");
            return -1;
        }
    }

    // Unix: `kill -2` (SIGINT) o `kill -15` (SIGTERM).
    private static async Task SendSignalAsync(int pid, string signal, CancellationToken ct)
    {
        try
        {
            using var kill = Process.Start(new ProcessStartInfo
            {
                FileName        = "kill",
                UseShellExecute = false,
                CreateNoWindow  = true,
                ArgumentList    = { signal, pid.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            });
            if (kill is not null) await kill.WaitForExitAsync(ct);
        }
        catch (System.ComponentModel.Win32Exception) { /* sin kill → cae al Kill por timeout */ }
    }

    private static bool HasExitedSafe(Process p)
    {
        try { return p.HasExited; }
        catch (InvalidOperationException) { return true; }
    }

    private static int SafeExitCode(Process p)
    {
        try { return p.ExitCode; }
        catch (InvalidOperationException) { return -1; }
    }
}

/// <summary>Resultado de <see cref="ProcessStop.StopAsync"/>: <c>Clean</c> = terminó con una señal; <c>Escalated</c> = hubo SIGTERM.</summary>
public readonly record struct ProcessStopResult(bool Clean, bool Escalated);
