using System.Diagnostics;
using Factum.Agent.Common;

namespace Factum.Agent.Services.Ios;

/// <summary>
/// <c>Factum.Agent --autoprueba-ios &lt;carpeta&gt;</c> (SDD ios-herramientas-windows §6.9): prueba de humo
/// de las herramientas de iPhone sin iPhone, con el código de producción (IosHelper, DvtRecorder,
/// IosErrors, AppleServiceProbe). Una línea por paso (<c>OK  …</c> / <c>FALLA  …</c>); devuelve 0
/// solo si pasan todos. Solo escribe dentro de &lt;carpeta&gt;: no arranca Kestrel ni toca DataDirectory.
/// </summary>
public static class IosSelfTest
{
    public const string Flag = "--autoprueba-ios";
    public const string FakeUdid = "0000-AUTOPRUEBA";

    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(60);

    public static async Task<int> RunAsync(string folder)
    {
        var failures = 0;
        void Ok(string text) => Console.WriteLine("OK  " + text);
        void Fail(string text) { failures++; Console.WriteLine("FALLA  " + text); }

        using var loggerFactory = LoggerFactory.Create(b => b
            .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; })
            .SetMinimumLevel(LogLevel.Information));
        var log = loggerFactory.CreateLogger("autoprueba-ios");
        var isWindows = OperatingSystem.IsWindows();

        string dir;
        try
        {
            dir = Path.GetFullPath(folder);
            Directory.CreateDirectory(dir);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FALLA  no se pudo crear la carpeta de la autoprueba '{folder}': {ex.Message}");
            return 1;
        }
        Console.WriteLine($"Autoprueba de iPhone de Tatana en {dir} ({(isWindows ? "Windows" : "macOS/Linux")})");

        // 1. Python con pymobiledevice3.
        var python = IosHelper.ResolvePython();
        if (python is not null) Ok($"Python con pymobiledevice3: {python}");
        else Fail("no hay un Python que importe pymobiledevice3");

        // 2. Versión fija (D6).
        if (python is not null)
        {
            var r = await RunPythonAsync(python, ["-c", "import importlib.metadata as m; print(m.version('pymobiledevice3'))"]);
            var version = r.Success ? VersionParser.Pymobiledevice3(r.Stdout) : null;
            if (version == IosHelper.ExpectedPymobiledevice3Version) Ok($"pymobiledevice3 {version}");
            else Fail($"pymobiledevice3 {version ?? "(no se pudo leer)"}; se esperaba {IosHelper.ExpectedPymobiledevice3Version}. {IosHelper.LastLine(r.Stderr)}");
        }
        else Fail("versión de pymobiledevice3: sin Python");

        // 3. ffmpeg.
        var ffmpeg = ToolResolver.Find(AgentTools.Ffmpeg)?.Path;
        if (ffmpeg is not null) Ok($"ffmpeg: {ffmpeg}");
        else Fail("no se encontró ffmpeg");

        // 4. Imports que usa el helper (en Windows, también pywin32 y lzfse).
        if (python is not null)
        {
            var modules = new List<string>();
            if (isWindows) modules.AddRange(["win32security", "lzfse", "pymobiledevice3.osu.win_util"]);
            modules.AddRange([
                "pymobiledevice3.remote.userspace_tunnel",
                "pymobiledevice3.services.dvt.instruments.screenshot",
                "pymobiledevice3.services.mobile_image_mounter",
                "pymobiledevice3.services.amfi",
            ]);
            var r = await RunPythonAsync(python, ["-c", "import " + string.Join(", ", modules)]);
            if (r.Success) Ok("imports: " + string.Join(", ", modules));
            else Fail("imports: " + (IosHelper.LastLine(r.Stderr) ?? $"exit {r.ExitCode}"));
        }
        else Fail("imports: sin Python");

        string? helper = null;
        try { helper = IosHelper.EnsureHelperFile(); Ok($"helper: {helper}"); }
        catch (Exception ex) { Fail($"no se pudo escribir el helper: {ex.Message}"); }

        // 5 y 6. Grabación sintética con el DvtRecorder real y MP4 legible.
        var mp4 = Path.Combine(dir, "autoprueba.mp4");
        if (python is not null && ffmpeg is not null && helper is not null)
        {
            try
            {
                if (File.Exists(mp4)) File.Delete(mp4);
                await using var rec = await DvtRecorder.StartAsync(python, helper, "synthetic", mp4, ffmpeg,
                    2.0, synthetic: true, log);
                await Task.Delay(TimeSpan.FromSeconds(4));
                var result = await rec.StopAsync();
                if (result.Ok && result.Frames >= 4 && result.ElapsedMs < 10_000)
                    Ok($"grabación sintética: {result.Frames} cuadros, stop en {result.ElapsedMs} ms, {new FileInfo(mp4).Length} bytes");
                else
                    Fail($"grabación sintética: ok={result.Ok}, {result.Frames} cuadros, stop en {result.ElapsedMs} ms{(result.Error is null ? "" : ", " + result.Error.Message)}");
            }
            catch (Exception ex)
            {
                Fail($"grabación sintética: {ex.Message}");
            }

            if (File.Exists(mp4))
            {
                var r = await ProcessRunner.RunArgumentListAsync(ffmpeg, ["-v", "error", "-i", mp4, "-f", "null", "-"],
                    timeout: StepTimeout);
                if (r.Success) Ok("el MP4 se lee entero con ffmpeg");
                else Fail("el MP4 no se puede leer: " + (IosHelper.LastLine(r.Stderr) ?? $"exit {r.ExitCode}"));
            }
            else Fail("no quedó el MP4 de la grabación sintética");
        }
        else
        {
            Fail("grabación sintética: faltan Python, ffmpeg o el helper");
            Fail("lectura del MP4: sin grabación");
        }

        // 7. Captura sin iPhone: tiene que fallar clasificada (y no salir 0, A6).
        if (python is not null && helper is not null)
        {
            var sw = Stopwatch.StartNew();
            var png = Path.Combine(dir, "x.png");
            var r = await RunPythonAsync(python, [helper, "screenshot", "--udid", FakeUdid, "--output", png],
                TimeSpan.FromSeconds(30));
            sw.Stop();
            var line = r.Stderr.Split('\n', StringSplitOptions.TrimEntries)
                .LastOrDefault(l => l.StartsWith(IosErrors.HelperErrorPrefix, StringComparison.Ordinal));
            var parsed = IosErrors.TryParseHelperError(line, out var code, out _);
            if (r.ExitCode == 2 && parsed && code is IosErrors.AppleServiceMissing or IosErrors.DeviceNotFound
                && sw.Elapsed < TimeSpan.FromSeconds(30))
                Ok($"captura sin iPhone: exit 2, {code}, {sw.ElapsedMilliseconds} ms");
            else
                Fail($"captura sin iPhone: exit {r.ExitCode}, código {(parsed ? code : "(sin TATANA_ERROR)")}, {sw.ElapsedMilliseconds} ms");
        }
        else Fail("captura sin iPhone: sin Python o helper");

        // 8. Sonda del servicio de Apple.
        try
        {
            var state = await new AppleServiceProbe().GetStateAsync();
            Ok($"servicio de Apple: {AppleServiceProbe.ToJson(state)}");
        }
        catch (Exception ex)
        {
            Fail($"servicio de Apple: {ex.Message}");
        }

        Console.WriteLine(failures == 0 ? "RESULTADO: OK" : $"RESULTADO: {failures} paso(s) con FALLA");
        loggerFactory.Dispose();
        return failures == 0 ? 0 : 1;
    }

    private static Task<ProcessResult> RunPythonAsync(string python, IReadOnlyList<string> args, TimeSpan? timeout = null) =>
        ProcessRunner.RunArgumentListAsync(python, args, environment: IosHelper.PythonEnvironment,
            timeout: timeout ?? StepTimeout);
}
