using System.Diagnostics;
using Factum.Agent.Common;
using Factum.Agent.Services.Ios;
using Microsoft.Extensions.Logging.Abstractions;

namespace Factum.Agent.Tests;

// T6 de la SDD ios-herramientas-windows: el DvtRecorder real con el origen sintético del helper
// (sin iPhone). Necesita python3 con Pillow y ffmpeg; si no están, el test queda Skip con el motivo.
public sealed class DvtRecorderTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "factum-dvt-" + Guid.NewGuid().ToString("N"));

    public DvtRecorderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [SyntheticRecorderFact]
    public async Task Graba_sintetico_y_para_por_stdin_en_menos_de_10_s()
    {
        var python = SyntheticRecorderFactAttribute.Python!;
        var ffmpeg = SyntheticRecorderFactAttribute.Ffmpeg!;
        var helper = IosHelper.EnsureHelperFile(_root);
        var mp4 = Path.Combine(_root, "salida con espacios é.mp4");

        await using var rec = await DvtRecorder.StartAsync(python, helper, "synthetic", mp4, ffmpeg, 4.0,
            synthetic: true, NullLogger.Instance);
        await Task.Delay(TimeSpan.FromSeconds(2));
        var sw = Stopwatch.StartNew();
        var result = await rec.StopAsync();
        sw.Stop();

        Assert.True(result.Ok, result.Error?.Message);
        Assert.True(result.Frames > 0);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"el stop tardó {sw.ElapsedMilliseconds} ms");
        Assert.True(new FileInfo(mp4).Length > 0);
    }

    [SyntheticRecorderFact]
    public async Task Sin_ffmpeg_falla_con_tools_missing_antes_de_READY()
    {
        var python = SyntheticRecorderFactAttribute.Python!;
        var helper = IosHelper.EnsureHelperFile(_root);
        var ex = await Assert.ThrowsAsync<IosException>(() => DvtRecorder.StartAsync(python, helper, "synthetic",
            Path.Combine(_root, "x.mp4"), Path.Combine(_root, "no-existe-ffmpeg"), 2.0, synthetic: true,
            NullLogger.Instance));
        Assert.Equal(IosErrors.ToolsMissing, ex.Code);
    }
}

public sealed class SyntheticRecorderFactAttribute : FactAttribute
{
    public static readonly string? Python;
    public static readonly string? Ffmpeg;
    private static readonly string? Reason;

    static SyntheticRecorderFactAttribute()
    {
        Ffmpeg = ToolResolver.Find(AgentTools.Ffmpeg)?.Path;
        var python = ToolResolver.Find(AgentTools.Python)?.Path;
        if (python is null) { Reason = "no hay python3 en el PATH"; return; }
        if (Ffmpeg is null) { Reason = "no hay ffmpeg en el PATH"; return; }
        var r = ProcessRunner.RunArgumentListAsync(python, ["-c", "import PIL"],
            environment: IosHelper.PythonEnvironment, timeout: TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
        if (!r.Success) { Reason = $"{python} no tiene Pillow"; return; }
        Python = python;
    }

    public SyntheticRecorderFactAttribute()
    {
        if (Reason is not null) Skip = "Grabador sintético: " + Reason;
    }
}
