using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Factum.Agent.Common;

namespace Factum.Agent.Services;

// Estado de una herramienta para /health.tools (SDD grabacion-android-windows §4.2, §6.6).
// Source va como string literal ("portable" | "path" | "homebrew") para no depender del
// converter de enums; los null no viajan (WhenWritingNull en Program.cs).
public sealed record ToolStatus(bool Found, string? Source = null, string? Path = null, string? Version = null);

// Inventario de las herramientas de Tatana para /health. La ruta se resuelve en cada llamada
// (solo File.Exists, instantáneo); la versión sale de un caché en memoria que se llena en
// segundo plano, así /health no se demora (el client lo sondea con timeout de 2 s).
public sealed class ToolInventory(ILogger<ToolInventory> log)
{
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(5);

    // Claves fijas en minúscula (las claves de diccionario no pasan por la naming policy).
    private static readonly (string Key, ToolSpec Spec, string Arg)[] Tools =
    [
        ("adb",    AgentTools.Adb,    "version"),
        ("scrcpy", AgentTools.Scrcpy, "--version"),
        ("ffmpeg", AgentTools.Ffmpeg, "-version"),
        ("python", AgentTools.Python, "--version"),
    ];

    // ruta → versión (null = se está obteniendo o falló).
    private readonly ConcurrentDictionary<string, string?> _versions = new(StringComparer.Ordinal);

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
            result[key] = new ToolStatus(true, SourceText(found.Source), found.Path,
                GetOrStartVersion(key, found.Path, arg));
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

    private async Task<string?> ReadVersionAsync(string key, string path, string arg)
    {
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
            psi.ArgumentList.Add(arg);

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
            var version = VersionParser.Parse(key, outText, errText);
            if (version is null) log.LogDebug("No se pudo leer la versión de {Tool} ({Path})", key, path);
            return version;
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "No se pudo leer la versión de {Tool} ({Path})", key, path);
            return null;
        }
    }
}

// Parsers de la salida de "--version" de cada herramienta (testeados).
internal static class VersionParser
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
