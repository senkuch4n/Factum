using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Factum.Agent.Common;
using Factum.Agent.Services.Ios;

namespace Factum.Agent.Services;

// Estado de una herramienta para /health.tools (SDD grabacion-android-windows §4.2, §6.6).
// Source va como string literal ("portable" | "path" | "homebrew") para no depender del
// converter de enums; los null no viajan (WhenWritingNull en Program.cs).
// Pymobiledevice3Version solo para "python" (ios-herramientas-windows §4.2): nombre JSON explícito
// para no depender de cómo parte el snake_case un nombre con dígito.
public sealed record ToolStatus(bool Found, string? Source = null, string? Path = null, string? Version = null,
    [property: JsonPropertyName("pymobiledevice3_version")] string? Pymobiledevice3Version = null);

// Inventario de las herramientas de Tatana para /health. La ruta se resuelve en cada llamada
// (solo File.Exists, instantáneo); la versión sale de un caché en memoria que se llena en
// segundo plano, así /health no se demora (el client lo sondea con timeout de 2 s).
public sealed class ToolInventory(ILogger<ToolInventory> log)
{
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(5);

    // Claves fijas en minúscula (las claves de diccionario no pasan por la naming policy).
    // Arg null = no se lee versión (uxplay: lanzarlo levanta GStreamer).
    private static readonly (string Key, ToolSpec Spec, string? Arg)[] Tools =
    [
        ("adb",    AgentTools.Adb,    "version"),
        ("scrcpy", AgentTools.Scrcpy, "--version"),
        ("ffmpeg", AgentTools.Ffmpeg, "-version"),
        ("python", AgentTools.Python, "--version"),
        ("uxplay", AgentTools.Uxplay, null),
    ];

    private static readonly string[] Pymobiledevice3VersionArgs =
        ["-c", "import importlib.metadata as m; print(m.version('pymobiledevice3'))"];

    // ruta → versión (null = se está obteniendo o falló).
    private readonly ConcurrentDictionary<string, string?> _versions = new(StringComparer.Ordinal);
    // ruta del python → versión de pymobiledevice3 (mismo criterio).
    private readonly ConcurrentDictionary<string, string?> _pmdVersions = new(StringComparer.Ordinal);

    public Dictionary<string, ToolStatus> Snapshot()
    {
        var result = new Dictionary<string, ToolStatus>(StringComparer.Ordinal);
        foreach (var (key, spec, arg) in Tools)
        {
            var found = ToolResolver.Find(spec);
            if (found is null)
            {
                result[key] = new ToolStatus(false);
                continue;
            }
            var version = arg is null ? null : GetOrStartVersion(key, found.Path, arg);
            var pmd = key == "python" ? GetOrStartPymobiledevice3Version(found.Path) : null;
            result[key] = new ToolStatus(true, SourceText(found.Source), found.Path, version, pmd);
        }
        return result;
    }

    // Arranca en segundo plano la obtención de las versiones, para que el diagnóstico ya las
    // tenga cuando el perito lo abre.
    public void WarmUp() => _ = Snapshot();

    internal static string SourceText(ToolSource s) => s switch
    {
        ToolSource.Portable => "portable",
        ToolSource.Path     => "path",
        ToolSource.Homebrew => "homebrew",
        _                   => s.ToString().ToLowerInvariant(),
    };

    private string? GetOrStartVersion(string key, string path, string arg)
    {
        if (_versions.TryGetValue(path, out var cached)) return cached;
        // Una sola vez por ruta: TryAdd gana uno solo aunque /health llegue en paralelo.
        if (_versions.TryAdd(path, null))
            _ = Task.Run(async () =>
            {
                var v = await ReadVersionAsync(key, path, arg);
                if (v is not null) _versions[path] = v;
            });
        return null;
    }

    private string? GetOrStartPymobiledevice3Version(string pythonPath)
    {
        if (_pmdVersions.TryGetValue(pythonPath, out var cached)) return cached;
        if (_pmdVersions.TryAdd(pythonPath, null))
            _ = Task.Run(async () =>
            {
                var (stdout, _) = await RunVersionAsync("pymobiledevice3", pythonPath, Pymobiledevice3VersionArgs) ?? ("", "");
                var v = VersionParser.Pymobiledevice3(stdout);
                if (v is null)
                {
                    log.LogDebug("No se pudo leer la versión de pymobiledevice3 ({Path})", pythonPath);
                    return;
                }
                _pmdVersions[pythonPath] = v;
                if (v != IosHelper.ExpectedPymobiledevice3Version)
                    log.LogWarning("pymobiledevice3 {Version} en {Path}; la versión probada es {Expected}",
                        v, pythonPath, IosHelper.ExpectedPymobiledevice3Version);
            });
        return null;
    }

    private async Task<string?> ReadVersionAsync(string key, string path, string arg)
    {
        var output = await RunVersionAsync(key, path, [arg]);
        if (output is null) return null;
        var version = VersionParser.Parse(key, output.Value.Stdout, output.Value.Stderr);
        if (version is null) log.LogDebug("No se pudo leer la versión de {Tool} ({Path})", key, path);
        return version;
    }

    // stdout/stderr de `<path> <args>` con timeout de 5 s; null si no arrancó o no terminó.
    private async Task<(string Stdout, string Stderr)?> RunVersionAsync(string key, string path, string[] args)
    {
        var arg = string.Join(' ', args);
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = path,
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding  = Encoding.UTF8,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            // Mismo entorno que el resto de los Python de Tatana (UTF-8, sin colores).
            if (key is "python" or "pymobiledevice3")
                foreach (var (k, v) in IosHelper.PythonEnvironment) psi.Environment[k] = v;

            // Process local (no ProcessRunner): hay que poder matarlo si se pasa del timeout.
            using var proc = new Process { StartInfo = psi };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) lock (stdout) stdout.AppendLine(e.Data); };
            proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) lock (stderr) stderr.AppendLine(e.Data); };
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            using var timeout = new CancellationTokenSource(VersionTimeout);
            try
            {
                await proc.WaitForExitAsync(timeout.Token);
                proc.WaitForExit();
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                log.LogWarning("{Tool} {Arg} no respondió en {Sec} s", key, arg, VersionTimeout.TotalSeconds);
                return null;
            }

            string outText, errText;
            lock (stdout) outText = stdout.ToString();
            lock (stderr) errText = stderr.ToString();
            return (outText, errText);
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "No se pudo leer la versión de {Tool} ({Path})", key, path);
            return null;
        }
    }
}

// Parsers de la salida de "--version" de cada herramienta (testeados).
internal static partial class VersionParser
{
    public static string? Parse(string key, string stdout, string stderr) => key switch
    {
        "adb"    => Adb(stdout),
        "scrcpy" => Scrcpy(stdout),
        "ffmpeg" => Ffmpeg(stdout),
        "python" => Python(stdout) ?? Python(stderr),
        _        => null,
    };

    // "Android Debug Bridge version 1.0.41\nVersion 37.0.1-13426479\nInstalled as …"
    // → línea que empieza con "Version " → el resto. Si no está, la 1ª línea.
    public static string? Adb(string output)
    {
        var lines = Lines(output);
        var v = lines.FirstOrDefault(l => l.StartsWith("Version ", StringComparison.Ordinal));
        if (v is not null) return NullIfEmpty(v["Version ".Length..].Trim());
        return lines.FirstOrDefault();
    }

    // "scrcpy 4.1 <https://github.com/Genymobile/scrcpy>" → "4.1"
    public static string? Scrcpy(string output)
    {
        var first = Lines(output).FirstOrDefault();
        if (first is null) return null;
        var tokens = first.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length >= 2 ? tokens[1] : null;
    }

    // "ffmpeg version <X> Copyright (c) …" → "<X>"
    public static string? Ffmpeg(string output)
    {
        var first = Lines(output).FirstOrDefault();
        if (first is null) return null;
        var tokens = first.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var i = Array.IndexOf(tokens, "version");
        return i >= 0 && i + 1 < tokens.Length ? tokens[i + 1] : null;
    }

    // Salida de `python -c "…print(m.version('pymobiledevice3'))"`: la primera línea no vacía que
    // parezca una versión ("10.7.4", "10.7.4.dev3+g1a2b"); un traceback u otra cosa → null.
    public static string? Pymobiledevice3(string output)
    {
        return Lines(output).FirstOrDefault(l => Pymobiledevice3Regex().IsMatch(l));
    }

    [GeneratedRegex(@"^\d+(\.\d+)*([a-z0-9.+-]*)$")]
    private static partial Regex Pymobiledevice3Regex();

    // "Python 3.11.9" → "3.11.9" (Python < 3.4 lo escribe en stderr).
    public static string? Python(string output)
    {
        var line = Lines(output).FirstOrDefault(l => l.StartsWith("Python ", StringComparison.Ordinal));
        return line is null ? null : NullIfEmpty(line["Python ".Length..].Trim());
    }

    private static List<string> Lines(string text) =>
        text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;
}
