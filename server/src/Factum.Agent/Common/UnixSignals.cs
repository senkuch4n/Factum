using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Factum.Agent.Common;

/// <summary>
/// Señales en Linux con <c>kill(2)</c> directo por P/Invoke (SDD tatana-linux D-T16, E13): no depende de que
/// exista el ejecutable <c>kill</c> ni de lanzar un proceso por señal. Si la biblioteca o el símbolo no
/// están, cae al ejecutable <c>kill</c>. Lo usan <see cref="ProcessStop"/> e <c>IosService</c> solo en Linux;
/// macOS sigue con el <c>kill</c> de siempre.
/// </summary>
public static class UnixSignals
{
    public const int SIGINT = 2;
    public const int SIGTERM = 15;
    public const int SIGKILL = 9;

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int NativeKill(int pid, int sig);

    private static int _nativeUnavailable; // 1 = libc/kill no se pudo cargar: se usa el ejecutable

    /// <summary>Manda <paramref name="signal"/> a <paramref name="pid"/>. true si la señal se entregó.</summary>
    public static bool TrySend(int pid, int signal)
    {
        if (pid <= 0 || OperatingSystem.IsWindows()) return false;
        if (Volatile.Read(ref _nativeUnavailable) == 0)
        {
            try
            {
                return NativeKill(pid, signal) == 0;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                Volatile.Write(ref _nativeUnavailable, 1);
            }
        }
        return SendWithExecutable(pid, signal);
    }

    /// <summary>Último errno de <see cref="TrySend"/> por P/Invoke (diagnóstico; 0 si no hubo).</summary>
    public static int LastError => Marshal.GetLastPInvokeError();

    private static bool SendWithExecutable(int pid, int signal)
    {
        try
        {
            using var kill = Process.Start(new ProcessStartInfo
            {
                FileName = "kill",
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList =
                {
                    "-" + signal.ToString(CultureInfo.InvariantCulture),
                    pid.ToString(CultureInfo.InvariantCulture),
                },
            });
            if (kill is null) return false;
            kill.WaitForExit(3000);
            return kill.HasExited && kill.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
