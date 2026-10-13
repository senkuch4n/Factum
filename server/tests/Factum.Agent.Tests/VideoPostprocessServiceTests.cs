using System.Diagnostics;
using System.Security.Cryptography;
using Factum.Agent.Common;
using Factum.Agent.Controllers;
using Factum.Agent.Models;
using Factum.Agent.Services;
using Factum.Agent.WebSockets;

namespace Factum.Agent.Tests;

/// <summary>
/// Post-proceso de grabaciones iOS (ios-grabacion-larga §5.3, §10.3). Los de integración usan ffmpeg real
/// ([FfmpegFact]) y un DataDirectory propio en el temporal; en Dispose se borra SOLO esa carpeta.
/// </summary>
public sealed class VideoPostprocessServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "factum-pp-" + Guid.NewGuid().ToString("N"));
    private readonly List<VideoPostprocessService> _services = [];

    public void Dispose()
    {
        foreach (var s in _services)
        {
            s.CancelAll("agent_stopping");
            try { s.WhenIdleAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult(); } catch { }
            s.Dispose();
        }
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    // ── Puras ────────────────────────────────────────────────────────────────

    [Fact]
    public void ComputeTimeout_PisoTechoYProporcional()
    {
        var o = new VideoPostprocessOptions();
        Assert.Equal(TimeSpan.FromSeconds(7200), VideoPostprocessService.ComputeTimeout(null, o));
        Assert.Equal(TimeSpan.FromSeconds(300), VideoPostprocessService.ComputeTimeout(60, o));
        Assert.Equal(TimeSpan.FromSeconds(2280), VideoPostprocessService.ComputeTimeout(720, o));
        Assert.Equal(TimeSpan.FromSeconds(7200), VideoPostprocessService.ComputeTimeout(3 * 3600, o));

        var custom = new VideoPostprocessOptions
        {
            TimeoutFactor = 2, TimeoutExtraSeconds = 10, TimeoutMinSeconds = 1, TimeoutMaxSeconds = 100,
        };
        Assert.Equal(TimeSpan.FromSeconds(50), VideoPostprocessService.ComputeTimeout(20, custom));
        Assert.Equal(TimeSpan.FromSeconds(100), VideoPostprocessService.ComputeTimeout(1000, custom));
    }

    [Fact]
    public void ExceedsThreshold_Casos()
    {
        Assert.False(VideoPostprocessService.ExceedsThreshold(99999, 0));
        Assert.False(VideoPostprocessService.ExceedsThreshold(null, 120));
        Assert.True(VideoPostprocessService.ExceedsThreshold(121, 120));
        Assert.False(VideoPostprocessService.ExceedsThreshold(120, 120));
    }

    [Fact]
    public void ParseFfmpegDuration_Casos()
    {
        const string stderr = """
            Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'source.mp4':
              Metadata:
                major_brand     : isom
              Duration: 00:12:00.00, start: 0.000000, bitrate: 11650 kb/s
              Stream #0:0[0x1](und): Video: h264 (High) (avc1 / 0x31637661), yuv420p, 1178x2556, 2 fps
            At least one output file must be specified
            """;
        Assert.Equal(720, VideoPostprocessService.ParseFfmpegDuration(stderr));
        Assert.Equal(3723.5, VideoPostprocessService.ParseFfmpegDuration("  Duration: 01:02:03.50, start: 0"));
        Assert.Null(VideoPostprocessService.ParseFfmpegDuration("At least one output file must be specified"));
        Assert.Null(VideoPostprocessService.ParseFfmpegDuration("  Duration: N/A, bitrate: N/A"));
        Assert.Null(VideoPostprocessService.ParseFfmpegDuration(null));
    }

    [Fact]
    public void ParseProgressPercent_Casos()
    {
        Assert.Equal(50, VideoPostprocessService.ParseProgressPercent("out_time_us=360000000", 720));
        Assert.Equal(50, VideoPostprocessService.ParseProgressPercent("out_time_ms=360000000", 720));
        Assert.Equal(99, VideoPostprocessService.ParseProgressPercent("out_time_us=720000000", 720));
        Assert.Equal(99, VideoPostprocessService.ParseProgressPercent("out_time_us=900000000", 720));
        Assert.Equal(0, VideoPostprocessService.ParseProgressPercent("out_time_us=-23220", 720));
        Assert.Null(VideoPostprocessService.ParseProgressPercent("out_time_us=360000000", null));
        Assert.Null(VideoPostprocessService.ParseProgressPercent("out_time_us=N/A", 720));
        Assert.Null(VideoPostprocessService.ParseProgressPercent("frame=12", 720));
    }

    [Fact]
    public void Normalize_VuelveAlDefaultCampoPorCampo()
    {
        var o = new VideoPostprocessOptions
        {
            BlendMaxSourceSeconds = -1, RifeMaxSourceSeconds = -5, TimeoutFactor = 0, TimeoutExtraSeconds = -1,
            TimeoutMinSeconds = 10, TimeoutMaxSeconds = 20, ProgressIntervalSeconds = 0,
        }.Normalize(null);
        Assert.Equal(VideoPostprocessOptions.DefaultBlendMaxSourceSeconds, o.BlendMaxSourceSeconds);
        Assert.Equal(VideoPostprocessOptions.DefaultRifeMaxSourceSeconds, o.RifeMaxSourceSeconds);
        Assert.Equal(VideoPostprocessOptions.DefaultTimeoutFactor, o.TimeoutFactor);
        Assert.Equal(VideoPostprocessOptions.DefaultTimeoutExtraSeconds, o.TimeoutExtraSeconds);
        Assert.Equal(10, o.TimeoutMinSeconds); // válidos: no se tocan
        Assert.Equal(20, o.TimeoutMaxSeconds);
        Assert.Equal(VideoPostprocessOptions.DefaultProgressIntervalSeconds, o.ProgressIntervalSeconds);

        var crossed = new VideoPostprocessOptions { TimeoutMinSeconds = 500, TimeoutMaxSeconds = 100 }.Normalize(null);
        Assert.Equal(VideoPostprocessOptions.DefaultTimeoutMinSeconds, crossed.TimeoutMinSeconds);
        Assert.Equal(VideoPostprocessOptions.DefaultTimeoutMaxSeconds, crossed.TimeoutMaxSeconds);

        var nan = new VideoPostprocessOptions { TimeoutFactor = double.NaN, TimeoutMaxSeconds = 0 }.Normalize(null);
        Assert.Equal(VideoPostprocessOptions.DefaultTimeoutFactor, nan.TimeoutFactor);
        Assert.Equal(VideoPostprocessOptions.DefaultTimeoutMaxSeconds, nan.TimeoutMaxSeconds);
    }

    [Theory]
    [InlineData(new[] { "grabacion_ios_1.mp4" }, true)]
    [InlineData(new[] { "a.mp4", "b.mp4" }, true)]
    [InlineData(new[] { "../a.mp4" }, false)]
    [InlineData(new[] { "sub/a.mp4" }, false)]
    [InlineData(new[] { "" }, false)]
    [InlineData(new string[0], false)]
    public void StatusController_ValidaNombres(string[] names, bool ok) =>
        Assert.Equal(ok, VideoPostprocessController.AreValid(names));

    [Fact]
    public void StatusController_MasDe50_Invalido() =>
        Assert.False(VideoPostprocessController.AreValid(Enumerable.Range(0, 51).Select(i => $"g{i}.mp4").ToArray()));

    [Fact]
    public void Start_Deshabilitado_OSinFfmpeg_NoPlaneaNada()
    {
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "grabacion_ios_x.mp4");
        File.WriteAllBytes(file, [1, 2, 3]);
        var sink = new FakeSink();
        var off = New(sink, o => o.Enabled = false, ffmpeg: "/no/existe/ffmpeg");
        Assert.Empty(off.Start(file, null));
        var noFfmpeg = New(sink, ffmpeg: null);
        Assert.Empty(noFfmpeg.Start(file, null));
        Assert.Null(noFfmpeg.BackgroundSince);
        Assert.Empty(sink.Snapshot());
    }

    // ── Integración (ffmpeg real) ────────────────────────────────────────────

    [FfmpegFact]
    public async Task GuardarNoEspera_ImportaEnseguidaYLaVarianteQuedaEnLaRaiz()
    {
        var sink = new FakeSink();
        var svc = New(sink);
        var original = await MakeVideoAsync("grabacion_ios_test.mp4", 20, "320x640");
        var hash = Sha(original);

        var labels = svc.Start(original, null);
        Assert.Equal([VideoPostprocessService.LabelBlend], labels);
        Assert.NotNull(svc.BackgroundSince);

        var store = new CaseEvidenceStore(new AgentOptions { DataDirectory = _root, MinFreeBytes = 0 });
        var caseId = Guid.NewGuid().ToString();
        var sw = Stopwatch.StartNew();
        var info = await store.ImportAsync(caseId, "grabacion_ios_test.mp4", default);
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"import tardó {sw.Elapsed}");
        Assert.Equal(hash, info.Sha256);

        var ready = await sink.WaitTerminalAsync("grabacion_ios_test.mp4", VideoPostprocessService.LabelBlend, TimeSpan.FromSeconds(60));
        Assert.Equal("ready", ready.State);
        Assert.Equal(100, ready.Progress);
        Assert.Equal("grabacion_ios_test_blend.mp4", ready.Filename);
        Assert.Equal("/files/grabacion_ios_test_blend.mp4", ready.Url);
        await svc.WhenIdleAsync(TimeSpan.FromSeconds(10));

        Assert.True(File.Exists(Path.Combine(_root, "grabacion_ios_test_blend.mp4")));
        var caseDir = Path.Combine(_root, "cases", caseId);
        Assert.Equal(["grabacion_ios_test.mp4"], Directory.GetFiles(caseDir).Select(Path.GetFileName));
        Assert.Equal(hash, Sha(Path.Combine(caseDir, "grabacion_ios_test.mp4")));
        Assert.False(Directory.Exists(Path.Combine(_root, ".postprocess", "grabacion_ios_test")));
        Assert.Null(svc.BackgroundSince);

        // Compatibilidad: también el evento viejo.
        await sink.WaitAsync(e => e.Type == "video_variant_ready", TimeSpan.FromSeconds(5));
        sink.AssertExactlyOneTerminalPerLabel("grabacion_ios_test.mp4", labels);

        var status = svc.GetStatus("grabacion_ios_test.mp4", out var known);
        Assert.True(known);
        Assert.Equal("ready", Assert.Single(status).State);
    }

    [FfmpegFact]
    public async Task Timeout_MataYBorraElParcial()
    {
        var sink = new FakeSink();
        var svc = New(sink, o => { o.TimeoutMinSeconds = 1; o.TimeoutMaxSeconds = 1; });
        var original = await MakeVideoAsync("grabacion_ios_largo.mp4", 120, "640x1280");

        svc.Start(original, null);
        var t = await sink.WaitTerminalAsync("grabacion_ios_largo.mp4", VideoPostprocessService.LabelBlend, TimeSpan.FromSeconds(30));
        Assert.Equal(("timeout", "timeout"), (t.State, t.Reason));
        await svc.WhenIdleAsync(TimeSpan.FromSeconds(10));
        Assert.False(File.Exists(Path.Combine(_root, "grabacion_ios_largo_blend.mp4")));
        Assert.False(Directory.Exists(Path.Combine(_root, ".postprocess", "grabacion_ios_largo")));
        Assert.True(File.Exists(original)); // el original no se toca
    }

    [FfmpegFact]
    public async Task Discard_CancelaYNoDejaNada()
    {
        var sink = new FakeSink();
        var svc = New(sink);
        var original = await MakeVideoAsync("grabacion_ios_quitar.mp4", 120, "640x1280");

        svc.Start(original, null);
        await sink.WaitAsync(e => e.Data is VideoVariantStatusDto { State: "running" }, TimeSpan.FromSeconds(30));
        File.Delete(original); // como DELETE /files: primero se borra, después Discard
        svc.Discard("grabacion_ios_quitar.mp4");

        var t = await sink.WaitTerminalAsync("grabacion_ios_quitar.mp4", VideoPostprocessService.LabelBlend, TimeSpan.FromSeconds(30));
        Assert.Equal(("cancelled", "removed"), (t.State, t.Reason));
        await svc.WhenIdleAsync(TimeSpan.FromSeconds(10));
        Assert.False(File.Exists(Path.Combine(_root, "grabacion_ios_quitar_blend.mp4")));
        Assert.False(Directory.Exists(Path.Combine(_root, ".postprocess", "grabacion_ios_quitar")));
    }

    [FfmpegFact]
    public async Task Mantenimiento_CancelaConAgentUpdate()
    {
        var sink = new FakeSink();
        var svc = New(sink);
        var original = await MakeVideoAsync("grabacion_ios_update.mp4", 120, "640x1280");

        svc.Start(original, null);
        Assert.NotNull(svc.BackgroundSince);
        svc.CancelAll("agent_update");
        var t = await sink.WaitTerminalAsync("grabacion_ios_update.mp4", VideoPostprocessService.LabelBlend, TimeSpan.FromSeconds(30));
        Assert.Equal(("cancelled", "agent_update"), (t.State, t.Reason));
        await svc.WhenIdleAsync(TimeSpan.FromSeconds(10));
        Assert.Null(svc.BackgroundSince);
        Assert.False(Directory.Exists(Path.Combine(_root, ".postprocess", "grabacion_ios_update")));
        Assert.True(File.Exists(original));
    }

    [FfmpegFact]
    public async Task Umbral_OmiteSinLanzarBlend()
    {
        var sink = new FakeSink();
        var svc = New(sink, o => o.BlendMaxSourceSeconds = 5);
        var original = await MakeVideoAsync("grabacion_ios_umbral.mp4", 20, "320x640");

        svc.Start(original, null);
        var t = await sink.WaitTerminalAsync("grabacion_ios_umbral.mp4", VideoPostprocessService.LabelBlend, TimeSpan.FromSeconds(30));
        Assert.Equal(("skipped", "too_long"), (t.State, t.Reason));
        Assert.DoesNotContain(sink.Snapshot(), e => e.Data is VideoVariantStatusDto { State: "running" });
        await svc.WhenIdleAsync(TimeSpan.FromSeconds(10));
        Assert.False(File.Exists(Path.Combine(_root, "grabacion_ios_umbral_blend.mp4")));
    }

    [FfmpegFact]
    public async Task OriginalCorrupto_FallaConEvento()
    {
        Directory.CreateDirectory(_root);
        var original = Path.Combine(_root, "grabacion_ios_roto.mp4");
        File.WriteAllBytes(original, RandomNumberGenerator.GetBytes(64 * 1024));
        var sink = new FakeSink();
        var svc = New(sink);

        svc.Start(original, null);
        var t = await sink.WaitTerminalAsync("grabacion_ios_roto.mp4", VideoPostprocessService.LabelBlend, TimeSpan.FromSeconds(30));
        Assert.Equal(("failed", "ffmpeg_error"), (t.State, t.Reason));
        Assert.False(string.IsNullOrEmpty(t.Message));
        Assert.True(t.Message!.Length <= 200);
        await svc.WhenIdleAsync(TimeSpan.FromSeconds(10));
        sink.AssertExactlyOneTerminalPerLabel("grabacion_ios_roto.mp4", [VideoPostprocessService.LabelBlend]);
        Assert.False(Directory.Exists(Path.Combine(_root, ".postprocess", "grabacion_ios_roto")));
    }

    [Fact]
    public async Task OriginalInexistente_FallaConSourceMissing()
    {
        Directory.CreateDirectory(_root);
        var sink = new FakeSink();
        // Con una ruta de ffmpeg cualquiera: no se llega a ejecutar.
        var svc = New(sink, ffmpeg: "/no/existe/ffmpeg");
        var labels = svc.Start(Path.Combine(_root, "grabacion_ios_nada.mp4"), null);
        Assert.Equal([VideoPostprocessService.LabelBlend], labels);
        var t = await sink.WaitTerminalAsync("grabacion_ios_nada.mp4", VideoPostprocessService.LabelBlend, TimeSpan.FromSeconds(30));
        Assert.Equal(("failed", "source_missing"), (t.State, t.Reason));
    }

    [Fact]
    public async Task SinEspacio_OmiteConInsufficientDisk()
    {
        Directory.CreateDirectory(_root);
        var original = Path.Combine(_root, "grabacion_ios_disco.mp4");
        File.WriteAllBytes(original, new byte[4096]);
        var sink = new FakeSink();
        var svc = New(sink, ffmpeg: "/no/existe/ffmpeg", free: 1000);
        svc.Start(original, null);
        var t = await sink.WaitTerminalAsync("grabacion_ios_disco.mp4", VideoPostprocessService.LabelBlend, TimeSpan.FromSeconds(30));
        Assert.Equal(("skipped", "insufficient_disk"), (t.State, t.Reason));
        await svc.WhenIdleAsync(TimeSpan.FromSeconds(10));
        Assert.False(Directory.Exists(Path.Combine(_root, ".postprocess", "grabacion_ios_disco")));
    }

    [Fact]
    public void MoverConElHandleAbierto_NoTira()
    {
        Directory.CreateDirectory(Path.Combine(_root, "cases", "x"));
        var src = Path.Combine(_root, "grabacion_ios_handle.mp4");
        File.WriteAllBytes(src, RandomNumberGenerator.GetBytes(10_000));
        using var handle = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var dst = Path.Combine(_root, "cases", "x", "grabacion_ios_handle.mp4");
        File.Move(src, dst);
        Assert.True(File.Exists(dst));
        // El handle sigue leyendo el contenido completo.
        var buf = new byte[10_000];
        handle.ReadExactly(buf);
        Assert.Equal(Sha(dst), Convert.ToHexStringLower(SHA256.HashData(buf)));
    }

    [Fact]
    public void EstadoTrasReinicio_KnownFalseConLoQueHayEnDisco()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(Path.Combine(_root, "grabacion_ios_viejo_blend.mp4"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(_root, "grabacion_ios_viejo_mci.mp4"), [1, 2, 3]);
        var svc = New(new FakeSink());
        var status = svc.GetStatus("grabacion_ios_viejo.mp4", out var known);
        Assert.False(known);
        Assert.Equal(["Suavizado", "MCI"], status.Select(s => s.Label));
        var blend = status[0];
        Assert.Equal(("ready", 100, "grabacion_ios_viejo_blend.mp4", "/files/grabacion_ios_viejo_blend.mp4"),
            (blend.State, blend.Progress, blend.Filename, blend.Url));

        Assert.Empty(svc.GetStatus("grabacion_ios_otro.mp4", out var known2));
        Assert.False(known2);
    }

    [Fact]
    public void Discard_SinTrabajo_BorraSoloLasVariantesDeEseOriginal()
    {
        Directory.CreateDirectory(_root);
        foreach (var n in new[] { "grabacion_ios_a_blend.mp4", "grabacion_ios_a_rife.mp4", "grabacion_ios_a_mci.mp4",
                     "grabacion_ios_b_blend.mp4", "otro_blend.mp4" })
            File.WriteAllBytes(Path.Combine(_root, n), [1]);
        var svc = New(new FakeSink());
        svc.Discard("grabacion_ios_a.mp4");
        svc.Discard("otro.mp4");           // no cumple el patrón: no toca nada
        svc.Discard("../grabacion_ios_b.mp4"); // inválido: no toca nada
        Assert.Equal(["grabacion_ios_b_blend.mp4", "otro_blend.mp4"],
            Directory.GetFiles(_root).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void CleanupWorkDirectory_SoloBorraPostprocess()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".postprocess", "grabacion_ios_z"));
        File.WriteAllBytes(Path.Combine(_root, ".postprocess", "grabacion_ios_z", "source.mp4"), [1]);
        File.WriteAllBytes(Path.Combine(_root, "grabacion_ios_z.mp4"), [1]);
        Assert.Equal(1, VideoPostprocessService.CleanupWorkDirectory(_root));
        Assert.False(Directory.Exists(Path.Combine(_root, ".postprocess")));
        Assert.True(File.Exists(Path.Combine(_root, "grabacion_ios_z.mp4")));
        Assert.Equal(0, VideoPostprocessService.CleanupWorkDirectory(_root));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private VideoPostprocessService New(FakeSink sink, Action<VideoPostprocessOptions>? configure = null,
        string? ffmpeg = "<auto>", long? free = null)
    {
        var o = new VideoPostprocessOptions { ProgressIntervalSeconds = 1 };
        configure?.Invoke(o);
        var path = ffmpeg == "<auto>" ? FfmpegFactAttribute.Ffmpeg : ffmpeg;
        var svc = new VideoPostprocessService(
            new AgentOptions { DataDirectory = _root, MinFreeBytes = 0, VideoPostprocess = o },
            sink, new FixedProbe(free), null, null, path, rifePath: null);
        _services.Add(svc);
        return svc;
    }

    private async Task<string> MakeVideoAsync(string name, int seconds, string size)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, name);
        var r = await ProcessRunner.RunArgumentListAsync(FfmpegFactAttribute.Ffmpeg!,
            ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", $"testsrc2=size={size}:rate=2",
                "-t", seconds.ToString(), "-c:v", "libx264", "-pix_fmt", "yuv420p", path],
            timeout: TimeSpan.FromSeconds(60));
        Assert.True(r.Success, r.Stderr);
        return path;
    }

    private static string Sha(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(fs));
    }

    private sealed class FixedProbe(long? free) : IAgentDiskProbe
    {
        public long? GetAvailableFreeBytes(string path) => free;
    }

    private sealed class FakeSink : IAgentEventSink
    {
        private readonly List<AgentEvent> _events = [];

        public Task BroadcastAsync(AgentEvent evt, CancellationToken ct = default)
        {
            lock (_events) _events.Add(evt);
            return Task.CompletedTask;
        }

        public List<AgentEvent> Snapshot()
        {
            lock (_events) return [.. _events];
        }

        public async Task<AgentEvent> WaitAsync(Func<AgentEvent, bool> predicate, TimeSpan max)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < max)
            {
                var hit = Snapshot().FirstOrDefault(predicate);
                if (hit is not null) return hit;
                await Task.Delay(50);
            }
            throw new TimeoutException("No llegó el evento esperado. Eventos: " +
                string.Join(" | ", Snapshot().Select(e => e.Type + " " + e.Data)));
        }

        public async Task<VideoVariantStatusDto> WaitTerminalAsync(string original, string label, TimeSpan max)
        {
            var e = await WaitAsync(e => e.Data is VideoVariantStatusDto d && d.OriginalFilename == original &&
                                         d.Label == label && IsTerminal(d.State), max);
            return (VideoVariantStatusDto)e.Data!;
        }

        public void AssertExactlyOneTerminalPerLabel(string original, IEnumerable<string> labels)
        {
            var statuses = Snapshot().Select(e => e.Data).OfType<VideoVariantStatusDto>()
                .Where(d => d.OriginalFilename == original).ToList();
            foreach (var l in labels)
                Assert.Single(statuses, d => d.Label == l && IsTerminal(d.State));
            Assert.DoesNotContain(statuses, d => d.State == "pending"); // pending no viaja por WS
        }

        private static bool IsTerminal(string s) => s is "ready" or "failed" or "timeout" or "skipped" or "cancelled";
    }
}

/// <summary>Fact que queda Skip si no hay ffmpeg en esta PC (ios-grabacion-larga §10.3).</summary>
public sealed class FfmpegFactAttribute : FactAttribute
{
    public static readonly string? Ffmpeg = ToolResolver.Find(AgentTools.Ffmpeg)?.Path;

    public FfmpegFactAttribute()
    {
        if (Ffmpeg is null) Skip = "ffmpeg no está instalado";
    }
}
