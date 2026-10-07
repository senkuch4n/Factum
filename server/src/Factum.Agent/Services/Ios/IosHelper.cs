using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Factum.Agent.Common;

namespace Factum.Agent.Services.Ios;

/// <summary>
/// Python + helper de iPhone (SDD ios-herramientas-windows §6.1): escribe ios_helper.py (recurso
/// embebido) en %TEMP%/tatana/ios_helper_&lt;sha8&gt;.py, resuelve el Python con pymobiledevice3,
/// arma los ProcessStartInfo (ArgumentList + UTF-8 + entorno) y corre los subcomandos cortos.
/// Estático: lo usan IosService, DvtRecorder y la autoprueba (sin DI).
/// </summary>
public static class IosHelper
{
    /// <summary>Versión de pymobiledevice3 probada (D6). Otra versión solo genera un Warning.</summary>
    public const string ExpectedPymobiledevice3Version = "10.7.4";

    public const string ResourceName = "Factum.Agent.Services.Ios.ios_helper.py";

    /// <summary>Entorno de todo proceso Python que lanza Tatana (cierra H16: acentos y colores).</summary>
    public static readonly IReadOnlyDictionary<string, string> PythonEnvironment = new Dictionary<string, string>
    {
        ["PYTHONUTF8"]              = "1",
        ["PYTHONIOENCODING"]        = "utf-8",
        ["NO_COLOR"]                = "1",
        ["TERM"]                    = "dumb",
        ["PYTHONDONTWRITEBYTECODE"] = "1",
    };

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan NotFoundRetry = TimeSpan.FromSeconds(60);

    private static readonly Lazy<byte[]> ScriptBytes = new(LoadScript);
    private static readonly object PythonGate = new();
    private static string? _python;
    private static DateTimeOffset _pythonMissingUntil = DateTimeOffset.MinValue;
    private static string? _helperPath;

    // ── Helper en disco ────────────────────────────────────────────────────────────────

    public static byte[] Script => ScriptBytes.Value;

    /// <summary>Primeros 8 hex del SHA-256 del helper: un nombre por versión (Tatana viejo y nuevo no se pisan).</summary>
    public static string ScriptSha8 => Convert.ToHexStringLower(SHA256.HashData(Script))[..8];

    public static string HelperFileName => $"ios_helper_{ScriptSha8}.py";

    /// <summary>Escribe el helper en &lt;tempBase&gt;/tatana/ si no está y devuelve la ruta absoluta.</summary>
    public static string EnsureHelperFile(string? tempBase = null)
    {
        if (tempBase is null && _helperPath is not null && File.Exists(_helperPath)) return _helperPath;

        var dir = Path.Combine(tempBase ?? Path.GetTempPath(), "tatana");
        Directory.CreateDirectory(dir);
        var path = Path.GetFullPath(Path.Combine(dir, HelperFileName));
        var expected = Script;
        if (!File.Exists(path) || new FileInfo(path).Length != expected.Length)
        {
            // Escritura atómica: otro Tatana (u otra llamada) puede estar leyéndolo.
            var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllBytes(tmp, expected);
            try { File.Move(tmp, path, overwrite: true); }
            catch (IOException) { try { File.Delete(tmp); } catch { } }
        }
        if (tempBase is null) _helperPath = path;
        return path;
    }

    private static byte[] LoadScript()
    {
        using var stream = typeof(IosHelper).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Falta el recurso embebido {ResourceName}");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    // ── Python ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Python con pymobiledevice3: ToolResolver (portátil → PATH → Homebrew) y, si no importa
    /// pymobiledevice3, los candidatos de siempre. Se cachea; si no hay ninguno, null (se reintenta
    /// a los 60 s para no lanzar procesos en cada /health).
    /// </summary>
    public static string? ResolvePython()
    {
        lock (PythonGate)
        {
            if (_python is not null) return _python;
            if (DateTimeOffset.UtcNow < _pythonMissingUntil) return null;
        }

        string? found = null;
        foreach (var candidate in PythonCandidates())
        {
            if (ImportsPymobiledevice3(candidate)) { found = candidate; break; }
        }

        lock (PythonGate)
        {
            if (found is not null) _python = found;
            else _pythonMissingUntil = DateTimeOffset.UtcNow + NotFoundRetry;
            return _python;
        }
    }

    internal static IEnumerable<string> PythonCandidates()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var resolved = ToolResolver.Find(AgentTools.Python)?.Path;
        if (resolved is not null && seen.Add(resolved)) yield return resolved;
        foreach (var c in new[]
                 {
                     "python.exe", "python", "py",
                     "python3", "/usr/bin/python3", "/usr/local/bin/python3", "/opt/homebrew/bin/python3",
                 })
        {
            if (Path.IsPathRooted(c) && !File.Exists(c)) continue;
            if (seen.Add(c)) yield return c;
        }
    }

    private static bool ImportsPymobiledevice3(string python)
    {
        try
        {
            var r = ProcessRunner.RunArgumentListAsync(python, ["-c", "import pymobiledevice3"],
                    environment: PythonEnvironment, timeout: ProbeTimeout)
                .GetAwaiter().GetResult();
            return r.Success;
        }
        catch
        {
            return false;
        }
    }

    // ── Procesos ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ProcessStartInfo de Python con el entorno de §6.1: ArgumentList (rutas con espacios o
    /// acentos van como un solo argumento), UTF-8 en stdout/stderr y sin ventana.
    /// </summary>
    public static ProcessStartInfo CreateStartInfo(string python, IEnumerable<string> args,
        bool redirectStandardInput = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = python,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            RedirectStandardInput  = redirectStandardInput,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8,
        };
        // Sin BOM: si no, el primer renglón que lee el helper llega como "﻿stop".
        if (redirectStandardInput) psi.StandardInputEncoding = new UTF8Encoding(false);
        foreach (var a in args) psi.ArgumentList.Add(a);
        foreach (var (k, v) in PythonEnvironment) psi.Environment[k] = v;
        return psi;
    }

    /// <summary>
    /// Corre <c>python ios_helper.py &lt;subcomando&gt; &lt;args&gt;</c> y devuelve el stdout (JSON de una
    /// línea). Si falla: <see cref="IosException"/> armada desde la línea TATANA_ERROR, o
    /// <c>ios_capture_failed</c> con el último renglón de stderr.
    /// </summary>
    public static async Task<string> RunAsync(string python, string helperPath, string subcommand,
        IEnumerable<string> args, TimeSpan timeout, ILogger log, CancellationToken ct = default)
    {
        List<string> argv = [helperPath, subcommand, .. args];
        var r = await ProcessRunner.RunArgumentListAsync(python, argv, ct, PythonEnvironment, timeout);
        if (r.Success) return r.Stdout;

        var error = ErrorFromStderr(r.Stderr, r.ExitCode, OperatingSystem.IsWindows());
        log.LogWarning("Error de iPhone {Code}: {Detail}", error.Code, DetailForLog(r.Stderr) ?? $"exit {r.ExitCode}");
        log.LogDebug("Helper de iPhone ({Sub}) salió con {Exit}. stderr:\n{Stderr}", subcommand, r.ExitCode, r.Stderr);
        throw error;
    }

    /// <summary>Error del stderr del helper (testeable).</summary>
    internal static IosException ErrorFromStderr(string stderr, int exitCode, bool isWindows)
    {
        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            if (IosErrors.TryParseHelperError(lines[i], out var code, out var detail))
                return IosErrors.FromHelper(code, detail, isWindows);
        }
        if (exitCode == ProcessRunner.TimeoutExitCode)
            return IosErrors.Create(IosErrors.CaptureFailed, isWindows, "el iPhone no respondió a tiempo");
        if (exitCode == -1 && lines.Any(l => l.StartsWith("No se encontró", StringComparison.Ordinal)))
            return IosErrors.Create(IosErrors.ToolsMissing, isWindows);
        return IosErrors.Create(IosErrors.CaptureFailed, isWindows, LastLine(stderr) ?? $"código de salida {exitCode}");
    }

    // "detail" de la última línea TATANA_ERROR o, si no hay, el último renglón de stderr.
    internal static string? DetailForLog(string stderr)
    {
        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = lines.Length - 1; i >= 0; i--)
            if (IosErrors.TryParseHelperError(lines[i], out _, out var detail))
                return detail.Length > 0 ? detail : null;
        return LastLine(stderr);
    }

    internal static string? LastLine(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? null : lines[^1];
    }
}
