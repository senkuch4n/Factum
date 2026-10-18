using System.Collections.Concurrent;
using System.Diagnostics;
using Factum.Agent.Models;
using Factum.Agent.Services.Session;

namespace Factum.Agent.Tests.Session;

/// <summary>
/// Pista de cámara con ffmpeg y lavfi (grabacion-sesion-completa-f2 §5.3–§5.5, §14.1): nunca abre una cámara. Sin ffmpeg
/// se saltean. Cada ffmpeg que se lanza se cierra por su PID al final del test.
/// </summary>
public sealed class CameraTrackTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "factum-camera-track-" + Guid.NewGuid().ToString("N"));
    private readonly List<int> _pids = [];
    private readonly List<CameraTrack> _tracks = [];

    public void Dispose()
    {
        // Primero el cierre normal de cada pista; después, por las dudas, kill por el PID propio de cada ffmpeg.
        foreach (var t in _tracks)
        {
            try { t.StopAsync().WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult(); } catch { /* best effort */ }
            t.Dispose();
        }
        foreach (var pid in _pids)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (!p.HasExited)
                {
                    p.Kill();
                    p.WaitForExit(5000);
                }
            }
            catch { /* ya terminó */ }
        }
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static SessionOptions Opt(int stall = 0) => new()
    {
        CameraWidth = 320, CameraHeight = 240, CameraFps = 10, CameraCrf = 30, CameraStartTimeoutSeconds = 10,
        CameraStallSeconds = stall,
    };

    private (CameraTrack Track, ConcurrentQueue<CameraSegmentClosed> Lines, ConcurrentQueue<CameraExit> Exits,
        ConcurrentQueue<string> Order) New(int stall = 0)
    {
        var track = new CameraTrack(new CameraSpec("mock-cam", "Cámara simulada", false, "mock"), Opt(stall), lavfi: true,
            devices: null, micDevice: null, log: null) { StallPollInterval = TimeSpan.FromMilliseconds(500) };
        var lines = new ConcurrentQueue<CameraSegmentClosed>();
        var exits = new ConcurrentQueue<CameraExit>();
        var order = new ConcurrentQueue<string>();
        _tracks.Add(track);
        track.SegmentClosed += l => { lines.Enqueue(l); order.Enqueue("line"); };
        track.Exited += e => { exits.Enqueue(e); order.Enqueue("exit"); };
        return (track, lines, exits, order);
    }

    private async Task<CameraStart> StartAsync(CameraTrack track, int startNumber = 3, int seconds = 3)
    {
        var start = await track.StartAsync(_dir, startNumber, seconds);
        _pids.Add(start.Pid);
        return start;
    }

    [FfmpegFact]
    public async Task Lavfi_TramosContiguos_CierreConQ()
    {
        var (track, lines, exits, _) = New();
        var before = DateTime.UtcNow;
        var start = await StartAsync(track);
        Assert.InRange(start.AnchorAt, before, DateTime.UtcNow);
        // La línea Output #0 de lo que se graba: 1280×720 de lavfi acotado a 320 de ancho (alto proporcional).
        Assert.Equal((320, 180, 10), (start.Width, start.Height, start.Fps));
        Assert.False(start.Fallback);
        await SessionHarness.Eventually(() => lines.Count >= 2, 15000);
        var stop = await track.StopAsync();
        Assert.True(stop.Clean);
        track.Dispose();
        Assert.Empty(exits); // pedido: no es una salida no pedida

        var all = lines.ToList();
        Assert.True(all.Count >= 3, $"líneas: {all.Count}");
        Assert.Equal(Enumerable.Range(3, all.Count), all.Select(l => l.Index));
        for (var i = 1; i < all.Count; i++) Assert.Equal(all[i - 1].EndSeconds, all[i].StartSeconds, 3);
        Assert.Equal(0, all[0].StartSeconds, 3);
        Assert.InRange(all[0].EndSeconds, 2.5, 3.5);
        Assert.All(all[..^1], l => Assert.False(l.Final));
        Assert.True(all[^1].Final);
        Assert.All(all, l => Assert.True(File.Exists(l.PartPath) && new FileInfo(l.PartPath).Length > 0, l.PartPath));
        Assert.True(File.Exists(Path.Combine(_dir, "camara-preview.jpg")));
        using var p = TryGet(start.Pid);
        Assert.True(p is null || p.HasExited);
    }

    [FfmpegFact]
    public async Task Kill9_ExitedConElCsvLeidoAntes()
    {
        if (OperatingSystem.IsWindows()) return;
        var (track, lines, exits, order) = New();
        var start = await StartAsync(track);
        await SessionHarness.Eventually(() => lines.Count >= 1, 15000);
        await Task.Delay(500);
        using (var p = Process.GetProcessById(start.Pid)) p.Kill();
        await SessionHarness.Eventually(() => !exits.IsEmpty, 10000);
        Assert.Equal("exit", order.Last());
        Assert.Null(exits.Single().Detail);
        Assert.All(lines, l => Assert.False(l.Final));
        track.Dispose();
    }

    [FfmpegFact]
    public async Task Sigterm_SalidaNoPedida_YLuegoNoHayCierreDoble()
    {
        if (OperatingSystem.IsWindows()) return;
        var (track, lines, exits, _) = New();
        var start = await StartAsync(track, seconds: 60);
        using (var kill = Process.Start("kill", ["-15", start.Pid.ToString()])) await kill!.WaitForExitAsync();
        await SessionHarness.Eventually(() => !exits.IsEmpty, 10000);
        // SIGTERM: ffmpeg escribe la última línea (cierre limpio, salida 255): es la última y no hay siguiente.
        var last = Assert.Single(lines);
        Assert.True(last.Final);
        Assert.Equal(3, last.Index);
        var stop = await track.StopAsync();
        Assert.True(stop.Clean);
        Assert.Single(lines);
        track.Dispose();
    }

    [FfmpegFact]
    public async Task Vigilancia_ArchivoQuieto_Stalled()
    {
        // No se usa SIGSTOP: un hijo detenido traba el manejo de procesos de .NET en Unix (TryReapChild en bucle). Se
        // simula el archivo quieto desvinculando el part_file: ffmpeg sigue escribiendo en el inodo y el tamaño visto no
        // cambia. En Windows no se puede borrar un archivo abierto.
        if (OperatingSystem.IsWindows()) return;
        var (track, _, exits, _) = New(stall: 2);
        await StartAsync(track, seconds: 60);
        File.Delete(Path.Combine(_dir, CameraArgs.PartName(3)));
        await SessionHarness.Eventually(() => !exits.IsEmpty, 30000);
        Assert.Equal("stalled", exits.Single().Detail);
        track.Dispose();
    }

    [FfmpegFact]
    public async Task StopSinArrancar_EsLimpio()
    {
        var (track, _, _, _) = New();
        var s = await track.StopAsync();
        Assert.True(s.Clean);
        track.Dispose();
    }

    private static Process? TryGet(int pid)
    {
        try { return Process.GetProcessById(pid); }
        catch { return null; }
    }
}
