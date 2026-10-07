namespace Factum.Agent.Common;

// Resolución común de las herramientas externas de Tatana (adb, scrcpy, ffmpeg, …).
// Orden: tools/<dir>/<exe> junto al exe (portátil) → PATH (con PATHEXT en Windows) → Homebrew
// (solo fuera de Windows). Siempre devuelve una ruta ABSOLUTA, nunca un nombre pelado.
// SDD grabacion-android-windows §6.1; el algoritmo lo fijan los tests de Factum.Agent.Tests.

public enum ToolSource { Portable, Path, Homebrew }

public sealed record ToolSpec(string Key, string ToolDir, string UnixName, string? WinName);

public sealed record ToolResolution(string Path, ToolSource Source);

// Todo lo que depende del SO/proceso, inyectable para los tests.
public sealed record ToolEnvironment(
    string BaseDirectory, string? PathVariable, string? PathExt, bool IsWindows,
    IReadOnlyList<string> FallbackDirs)
{
    // Se arma en cada acceso: el PATH puede cambiar mientras Tatana corre y son solo lecturas
    // de variables de entorno.
    public static ToolEnvironment Current => new(
        AppContext.BaseDirectory,
        Environment.GetEnvironmentVariable("PATH"),
        Environment.GetEnvironmentVariable("PATHEXT"),
        OperatingSystem.IsWindows(),
        ["/opt/homebrew/bin", "/usr/local/bin"]);
}

public static class AgentTools
{
    public static readonly ToolSpec Adb     = new("adb",     "platform-tools", "adb",     "adb.exe");
    public static readonly ToolSpec Scrcpy  = new("scrcpy",  "scrcpy",         "scrcpy",  "scrcpy.exe");
    public static readonly ToolSpec Ffmpeg  = new("ffmpeg",  "ffmpeg",         "ffmpeg",  "ffmpeg.exe");
    public static readonly ToolSpec Python  = new("python",  "python-embed",   "python3", "python.exe");
    public static readonly ToolSpec Uxplay  = new("uxplay",  "uxplay",         "uxplay",  "uxplay.exe");
    public static readonly ToolSpec Qvh     = new("qvh",     "qvh",            "qvh",     null);
    public static readonly ToolSpec Rife    = new("rife",    "rife",           "rife-ncnn-vulkan", "rife-ncnn-vulkan.exe");
}

public static class ToolResolver
{
    private const string DefaultPathExt = ".COM;.EXE;.BAT;.CMD";

    public static ToolResolution? Find(ToolSpec spec) => Find(spec, ToolEnvironment.Current);

    internal static ToolResolution? Find(ToolSpec spec, ToolEnvironment env)
    {
        // 1. Nombre según el SO.
        var name = env.IsWindows ? (spec.WinName ?? spec.UnixName) : spec.UnixName;

        // 2. Portátil: tools/<dir>/<name> junto al exe.
        var portable = Path.Combine(env.BaseDirectory, "tools", spec.ToolDir, name);
        if (File.Exists(portable))
            return new ToolResolution(Path.GetFullPath(portable), ToolSource.Portable);

        // 3. PATH.
        var names = CandidateNames(name, env);
        var separator = env.IsWindows ? ';' : ':';
        foreach (var rawEntry in (env.PathVariable ?? string.Empty).Split(separator))
        {
            var dir = rawEntry.Trim().Trim('"');
            if (dir.Length == 0 || !Path.IsPathRooted(dir)) continue;
            foreach (var n in names)
            {
                var candidate = Path.Combine(dir, n);
                if (File.Exists(candidate))
                    return new ToolResolution(Path.GetFullPath(candidate), ToolSource.Path);
            }
        }

        // 4. Homebrew, solo fuera de Windows.
        if (!env.IsWindows)
        {
            foreach (var dir in env.FallbackDirs)
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate))
                    return new ToolResolution(Path.GetFullPath(candidate), ToolSource.Homebrew);
            }
        }

        return null;
    }

    // Nombres a probar en cada directorio del PATH. En Windows, sin extensión explícita, se
    // prueban las de PATHEXT, pero solo .exe/.com: Process.Start sin shell no debe correr
    // scripts (.bat/.cmd/.vbs/.js). Cada extensión va en minúscula y tal cual viene, porque el
    // FS de los tests (macOS/Linux) puede distinguir mayúsculas.
    private static List<string> CandidateNames(string name, ToolEnvironment env)
    {
        if (!env.IsWindows || Path.HasExtension(name)) return [name];

        var pathExt = string.IsNullOrWhiteSpace(env.PathExt) ? DefaultPathExt : env.PathExt;
        var result = new List<string>();
        foreach (var rawExt in pathExt.Split(';'))
        {
            var ext = rawExt.Trim();
            if (ext.Length == 0) continue;
            var lower = ext.ToLowerInvariant();
            if (lower is not (".exe" or ".com")) continue;
            foreach (var variant in new[] { lower, ext })
            {
                var n = name + variant;
                if (!result.Contains(n)) result.Add(n);
            }
        }
        return result;
    }
}
