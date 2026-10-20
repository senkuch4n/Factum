using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Factum.Agent.Common;
using Factum.Agent.Services.Session;

namespace Factum.Agent.Services.Linux;

/// <summary>
/// <c>Factum.Agent --autoprueba-linux &lt;carpeta&gt;</c> (SDD tatana-linux §5.7, D-T26): prueba de humo de lo propio de
/// Linux sin celular, con el código de producción. Una línea <c>OK  …</c> / <c>FALLA  …</c> por paso; sale con 0 solo si
/// pasan todos. Solo escribe dentro de &lt;carpeta&gt;: no arranca Kestrel ni toca DataDirectory. Fuera de Linux imprime
/// <c>FALLA  solo Linux</c> y sale con 1. La usan el humo de CI, los contenedores de distros y el paso 1 de la QA.
/// </summary>
public static class LinuxSelfTest
{
    public const string Flag = "--autoprueba-linux";
    public const string MinGlibc = "2.35";

    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(60);

    public static async Task<int> RunAsync(string folder)
    {
        if (!OperatingSystem.IsLinux())
        {
            Console.WriteLine("FALLA  solo Linux");
            return 1;
        }

        var failures = 0;
        void Ok(string text) => Console.WriteLine("OK  " + text);
        void Fail(string text) { failures++; Console.WriteLine("FALLA  " + text); }

        using var loggerFactory = LoggerFactory.Create(b => b
            .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; })
            .SetMinimumLevel(LogLevel.Warning));
        var log = loggerFactory.CreateLogger("autoprueba-linux");

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
        Console.WriteLine($"Autoprueba de Linux de Tatana {AgentVersion.Current} en {dir}");

        // 1. Herramientas portátiles (tools/ al lado del ejecutable).
        foreach (var spec in new[] { AgentTools.Adb, AgentTools.Scrcpy, AgentTools.Ffmpeg, AgentTools.Python })
        {
            var r = ToolResolver.Find(spec);
            if (r is { Source: ToolSource.Portable }) Ok($"{spec.Key} portátil: {r.Path}");
            else if (r is null) Fail($"{spec.Key}: no se encontró");
            else Fail($"{spec.Key}: no es el portátil de Tatana ({r.Source.ToString().ToLowerInvariant()}: {r.Path})");
        }
        var ffmpeg = ToolResolver.Find(AgentTools.Ffmpeg)?.Path;

        // 2. ffmpeg con PulseAudio y V4L2 (E11).
        if (ffmpeg is not null)
        {
            var r = await ProcessRunner.RunArgumentListAsync(ffmpeg, ["-hide_banner", "-devices"], timeout: StepTimeout);
            var text = r.Stdout + "\n" + r.Stderr;
            var missing = new[] { "pulse", "v4l2" }.Where(d => !HasDevice(text, d)).ToList();
            if (missing.Count == 0) Ok("ffmpeg trae los dispositivos pulse y v4l2");
            else Fail("ffmpeg no trae: " + string.Join(", ", missing));
        }
        else Fail("dispositivos de ffmpeg: sin ffmpeg");

        // 3. Grabación lavfi de 5 s con el comando de la cámara, cortada con SIGTERM.
        if (ffmpeg is not null)
        {
            try { await LavfiRecordingAsync(ffmpeg, dir, log, Ok, Fail); }
            catch (Exception ex) { Fail($"grabación lavfi: {ex.Message}"); }
        }
        else Fail("grabación lavfi: sin ffmpeg");

        // 4. kill(2) por P/Invoke a un hijo.
        try
        {
            using var sleep = Process.Start(new ProcessStartInfo("sleep") { UseShellExecute = false, ArgumentList = { "30" } })
                ?? throw new InvalidOperationException("no arrancó sleep");
            await Task.Delay(200);
            var sent = UnixSignals.TrySend(sleep.Id, UnixSignals.SIGTERM);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await sleep.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { try { sleep.Kill(); } catch { } }
            var code = sleep.HasExited ? sleep.ExitCode : -1;
            if (sent && code == 143) Ok("señal SIGTERM con kill(2): el hijo salió con 143");
            else Fail($"señal SIGTERM con kill(2): enviada={sent}, código {code}");
        }
        catch (Exception ex)
        {
            Fail($"señal SIGTERM: {ex.Message}");
        }

        // 5. Estado de la PC (no falla por chequeos missing: en CI son lo esperado).
        LinuxHostStatus? status = null;
        try
        {
            status = new LinuxHostProbe().GetStatus();
            var json = JsonSerializer.Serialize(status, JsonOptions);
            Ok($"estado de la PC: {json}");
        }
        catch (Exception ex)
        {
            Fail($"estado de la PC: {ex.Message}");
        }

        // 6. glibc mínima publicada (E5).
        var glibc = status?.GlibcVersion ?? LinuxNative.GlibcVersion();
        if (LinuxHostProbe.VersionAtLeast(glibc, MinGlibc)) Ok($"glibc {glibc} (mínimo {MinGlibc})");
        else Fail($"glibc {glibc ?? "(no se pudo leer)"}; hace falta {MinGlibc} o posterior");

        Console.WriteLine(failures == 0 ? "RESULTADO: OK" : $"RESULTADO: {failures} paso(s) con FALLA");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>Mismo formato que <c>/health</c>: snake_case_lower y sin nulls.</summary>
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Solo para imprimir en la consola: acentos legibles en vez de \u00E1.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // Línea de `ffmpeg -devices`: " D  pulse           Pulse audio input" / " DE v4l2 ..." / "video4linux2,v4l2".
    internal static bool HasDevice(string devicesOutput, string name) =>
        devicesOutput.Split('\n').Any(l =>
        {
            var parts = l.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 && parts[0].Length <= 2 && parts[0].All(c => c is 'D' or 'E' or '.')
                   && parts[1].Split(',').Contains(name, StringComparer.Ordinal);
        });

    private static async Task LavfiRecordingAsync(string ffmpeg, string dir, ILogger log, Action<string> ok, Action<string> fail)
    {
        var staging = Path.Combine(dir, "camara-lavfi");
        Directory.CreateDirectory(staging);
        var args = CameraArgs.Build(new CameraArgsInput(CameraArgs.BackendLavfi, "", WithAudio: false, Mic: null,
            Mode: null, Width: 640, Height: 360, Fps: 15, Crf: 30, SegmentSeconds: 3600, StartNumber: 0,
            StagingDir: staging));
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("no arrancó ffmpeg");
        proc.OutputDataReceived += (_, _) => { };
        proc.ErrorDataReceived += (_, _) => { };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await Task.Delay(TimeSpan.FromSeconds(5));
        if (proc.HasExited)
        {
            fail($"grabación lavfi: ffmpeg terminó antes de tiempo (código {proc.ExitCode})");
            return;
        }
        var stop = await ProcessStop.StopAsync(proc, TimeSpan.FromSeconds(15), log, "ffmpeg (autoprueba)",
            unixSignal: ProcessStop.SessionSignal);
        if (stop.Clean) ok("grabación lavfi cortada con SIGTERM: cerró limpio");
        else fail("grabación lavfi: ffmpeg no respondió a SIGTERM y hubo que matarlo");

        var mkv = Path.Combine(staging, CameraArgs.PartName(0));
        if (!File.Exists(mkv))
        {
            fail("grabación lavfi: no quedó el MKV");
            return;
        }
        var probe = await FfmpegProbe.ProbeAsync(ffmpeg, mkv, CancellationToken.None);
        if (probe.DurationSeconds is > 0 && probe.HasVideo) ok($"MKV legible: {probe.DurationSeconds:0.0} s de video");
        else fail("grabación lavfi: el MKV no tiene duración o video");
    }
}
