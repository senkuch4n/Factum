using System.Diagnostics;

namespace Factum.Agent.Common;

// Cierre limpio de un proceso de consola (scrcpy) con fallback a Kill
// (SDD grabacion-android-windows §6.3).
public static class ProcessStop
{
    private static readonly TimeSpan HelperTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan AfterFailedSignalWait = TimeSpan.FromSeconds(1);

    // true = cerró limpio con la señal; false = hubo que matarlo.
    public static async Task<bool> StopGracefullyAsync(Process p, TimeSpan timeout, ILogger log,
        string name, CancellationToken ct = default)
    {
        if (HasExitedSafe(p)) return true;

        var wait = timeout;
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
            await SendSigintAsync(p.Id, ct);
        }

        using (var timeoutCts = new CancellationTokenSource(wait))
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token))
        {
            try
            {
                await p.WaitForExitAsync(linked.Token);
                var exit = SafeExitCode(p);
                log.LogInformation("{Name} detenido limpiamente (exit {Code})", name, exit);
                return true;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout: se fuerza abajo.
            }
        }

        try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* ya terminó */ }
        try { await p.WaitForExitAsync(ct); } catch (InvalidOperationException) { }
        log.LogWarning("{Name} no respondió a la señal; se forzó el cierre", name);
        return false;
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

    // Unix: SIGINT con `kill -2`, igual que antes de esta HU.
    private static async Task SendSigintAsync(int pid, CancellationToken ct)
    {
        try
        {
            using var sigint = Process.Start(new ProcessStartInfo
            {
                FileName        = "kill",
                UseShellExecute = false,
                CreateNoWindow  = true,
                ArgumentList    = { "-2", pid.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            });
            if (sigint is not null) await sigint.WaitForExitAsync(ct);
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
