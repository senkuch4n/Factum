using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Factum.Agent.Common;

// Ctrl+C "de verdad" a un proceso de consola en Windows (SDD grabacion-android-windows §6.3, DP2).
//
// GenerateConsoleCtrlEvent(CTRL_C_EVENT) solo llega a los procesos que comparten la consola de
// quien lo llama. scrcpy corre en su propia consola oculta (CreateNoWindow), así que lo hace un
// proceso auxiliar de vida corta: el mismo Factum.Agent.exe en modo `--ctrl-c <pid>`, que suelta
// su consola, se pega a la de scrcpy, se protege del Ctrl+C y lo genera. Hacerlo dentro del
// agente le rompería su ventana de log y el ConsoleLifetime de ASP.NET (que apagaría Tatana).
public static class WindowsConsoleSignal
{
    public const string HelperFlag = "--ctrl-c";

    private const uint CtrlCEvent = 0;

    // Rama del auxiliar. Códigos: 0 ok, 1 no es Windows o pid inválido, 2 AttachConsole falló,
    // 3 GenerateConsoleCtrlEvent falló.
    public static int RunHelper(string pidArg)
    {
        if (!OperatingSystem.IsWindows()) return 1;
        if (!uint.TryParse(pidArg, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid == 0)
            return 1;
        return SendCtrlC(pid);
    }

    [SupportedOSPlatform("windows")]
    private static int SendCtrlC(uint pid)
    {
        FreeConsole();
        if (!AttachConsole(pid)) return 2;
        SetConsoleCtrlHandler(IntPtr.Zero, true);
        var ok = GenerateConsoleCtrlEvent(CtrlCEvent, 0);
        FreeConsole();
        return ok ? 0 : 3;
    }

    // DP12: el atributo "ignorar Ctrl+C" se hereda a los hijos. Si algún lanzador lo dejó
    // encendido, scrcpy lo heredaría y no cortaría nunca con el Ctrl+C del auxiliar.
    public static void EnableCtrlCForChildren()
    {
        if (!OperatingSystem.IsWindows()) return;
        try { SetConsoleCtrlHandler(IntPtr.Zero, false); } catch { /* defensivo: nunca impide arrancar */ }
    }

    // Función pura (testeada): cómo lanzar el auxiliar. En desarrollo (`dotnet Factum.Agent.dll`)
    // hay que pasarle el dll; con el exe publicado, el exe solo.
    public static (string FileName, IReadOnlyList<string> Arguments) BuildHelperCommand(
        string processPath, string? entryAssemblyPath, int pid)
    {
        var pidText = pid.ToString(CultureInfo.InvariantCulture);
        var exeName = GetFileNameWithoutExtensionAnySeparator(processPath);
        if (string.Equals(exeName, "dotnet", StringComparison.OrdinalIgnoreCase))
            return (processPath, [entryAssemblyPath ?? string.Empty, HelperFlag, pidText]);
        return (processPath, [HelperFlag, pidText]);
    }

    // Path.GetFileNameWithoutExtension en macOS/Linux no reconoce '\' como separador; los tests
    // corren ahí con rutas de Windows, así que se cortan los dos separadores a mano.
    private static string GetFileNameWithoutExtensionAnySeparator(string path)
    {
        var cut = path.LastIndexOfAny(['\\', '/']);
        var file = cut >= 0 ? path[(cut + 1)..] : path;
        return Path.GetFileNameWithoutExtension(file);
    }

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint dwProcessId);

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleCtrlHandler(IntPtr handlerRoutine, [MarshalAs(UnmanagedType.Bool)] bool add);

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);
}
