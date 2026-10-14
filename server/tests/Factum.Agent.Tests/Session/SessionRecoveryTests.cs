using System.Diagnostics;
using Factum.Agent.Common;
using Factum.Agent.Models;
using Factum.Agent.Services;
using Factum.Agent.Services.Session;

namespace Factum.Agent.Tests.Session;

/// <summary>
/// Recuperación al arrancar (grabacion-sesion-completa §5.10, §4.6). Los videos se generan con ffmpeg (lavfi): sin
/// ffmpeg, los tests se saltean. Todo en una carpeta temporal propia.
/// </summary>
public sealed class SessionRecoveryTests : IDisposable
{
    private readonly SessionHarness _h = new();
    private readonly SessionRecovery _recovery;
    private static string? Ffmpeg => FfmpegFactAttribute.Ffmpeg;

    public SessionRecoveryTests()
    {
        _recovery = new SessionRecovery(_h.Journal, _h.Closer,
            new StayAwakeKeeper(false, null, (_, _) => Task.FromResult(new ProcessResult(0, "0", ""))),
            _h.Recorder, _h.Recovery, _h.Sink, new RecordingOptions().Normalize(null), TimeProvider.System, null,
            mock: false, isConnected: s => Task.FromResult(_h.Connected.Contains(s)), findProcess: null);
    }

    public void Dispose() => _h.Dispose();

    private static readonly DateTime T0 = DateTime.UtcNow.AddMinutes(-5);

    private SessionJournalDoc Journal(params JournalSegment[] segments)
    {
        var doc = new SessionJournalDoc
        {
            SessionId = Guid.NewGuid().ToString("N"),
            CaseId = SessionHarness.CaseId,
            Serial = SessionHarness.Serial,
            SegmentMinutes = 30,
            Audio = "device",
            State = SessionStates.Recording,
            Segments = [.. segments],
            LastAliveAt = T0.AddSeconds(30),
        };
        _h.Journal.Save(doc, DateTime.UtcNow);
        return doc;
    }

    private static JournalSegment Open(int index, SegmentProcess? process = null) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Index = index,
        Filename = SessionNames.SegmentName("pantalla", index, new DateTime(2026, 10, 14, 14, 0, index)),
        State = SegmentStates.Recording,
        StartedAt = T0,
        Process = process,
    };

    private string Staging(JournalSegment s) => _h.Journal.StagingPath(SessionHarness.CaseId, s.Filename);
    private string Final(string name) => Path.Combine(_h.Store.CaseDir(SessionHarness.CaseId), name);

    private static async Task MakeMkvAsync(string path, int seconds)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var r = await ProcessRunner.RunArgumentListAsync(Ffmpeg!,
            ["-hide_banner", "-y", "-f", "lavfi", "-i", $"testsrc2=size=320x240:rate=10", "-t", seconds.ToString(),
             "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", path], timeout: TimeSpan.FromMinutes(1));
        Assert.True(r.Success, r.Stderr);
    }

    [FfmpegFact]
    public async Task MkvValido_SeGuardaRecuperado_Interrupted()
    {
        var seg = Open(1);
        Journal(seg);
        await MakeMkvAsync(Staging(seg), 3);

        Assert.Equal(1, await _recovery.RunAsync());
        var doc = _h.Doc();
        var s = doc.Segments[0];
        Assert.Equal("saved", s.State);
        Assert.True(s.Recovered);
        Assert.Equal("tatana_cerrado", s.StopReason);
        Assert.Equal("ok", s.Validation);
        Assert.Null(s.Process);
        Assert.InRange(s.DurationSeconds!.Value, 2.5, 3.5);
        Assert.Equal(T0.AddSeconds(s.DurationSeconds.Value), s.StoppedAt!.Value, TimeSpan.FromMilliseconds(200));
        Assert.True(File.Exists(Final(s.Filename)));
        Assert.False(File.Exists(Staging(seg)));
        Assert.Equal("interrupted", doc.State);
        Assert.Equal("tatana_cerrado", doc.StopReason);
        var gap = Assert.Single(doc.Gaps);
        Assert.Equal("tatana_cerrado", gap.Reason);
        Assert.False(gap.SegmentLost);
        Assert.Single(_h.Sink.OfType("session_segment_closed"));
    }

    [FfmpegFact]
    public async Task MkvTruncado_SeRemuxea()
    {
        var seg = Open(1);
        Journal(seg);
        var full = Path.Combine(_h.Root, "completo.mkv");
        await MakeMkvAsync(full, 8);
        var bytes = await File.ReadAllBytesAsync(full);
        Directory.CreateDirectory(Path.GetDirectoryName(Staging(seg))!);
        await File.WriteAllBytesAsync(Staging(seg), bytes[..(int)(bytes.Length * 0.6)]);

        await _recovery.RunAsync();
        var s = _h.Doc().Segments[0];
        Assert.Equal("saved", s.State);
        Assert.True(s.Recovered);
        Assert.True(s.DurationSeconds > 1, $"duración {s.DurationSeconds}");
    }

    [Fact]
    public async Task CeroBytes_Descartado_SegmentLost()
    {
        var seg = Open(1);
        Journal(seg);
        Directory.CreateDirectory(Path.GetDirectoryName(Staging(seg))!);
        await File.WriteAllBytesAsync(Staging(seg), []);

        await _recovery.RunAsync();
        var doc = _h.Doc();
        Assert.Equal("discarded", doc.Segments[0].State);
        Assert.False(File.Exists(Staging(seg)));
        Assert.True(Assert.Single(doc.Gaps).SegmentLost);
        Assert.Equal(doc.LastAliveAt, doc.Segments[0].StoppedAt); // sin duración: el último latido
    }

    [FfmpegFact]
    public async Task Ilegible_VaADescartes()
    {
        var seg = Open(1);
        Journal(seg);
        Directory.CreateDirectory(Path.GetDirectoryName(Staging(seg))!);
        await File.WriteAllBytesAsync(Staging(seg), System.Security.Cryptography.RandomNumberGenerator.GetBytes(4096));

        await _recovery.RunAsync();
        Assert.Equal("discarded", _h.Doc().Segments[0].State);
        Assert.True(File.Exists(Path.Combine(_h.Options.DataDirectory, "descartes", seg.Filename)));
    }

    [Fact]
    public async Task DiarioIlegible_SeRenombraSinTocarNada()
    {
        var dir = _h.Journal.DirFor(SessionHarness.CaseId);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "sesion.json"), "{ roto");
        var mkv = Path.Combine(dir, "sesion_pantalla_001_20261014_140001.mkv");
        await File.WriteAllBytesAsync(mkv, [1, 2, 3]);
        await _recovery.RunAsync();
        Assert.True(File.Exists(mkv));
        Assert.Contains(Directory.GetFiles(dir), f => Path.GetFileName(f).StartsWith("sesion.json.ilegible-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WaitingDevice_ConservaElHuecoDeDesconexion()
    {
        var doc = new SessionJournalDoc
        {
            SessionId = Guid.NewGuid().ToString("N"), CaseId = SessionHarness.CaseId, Serial = SessionHarness.Serial,
            SegmentMinutes = 30, State = SessionStates.WaitingDevice, StopReason = "desconexion",
            Gaps = [new SessionGapDto { Id = "g1", From = T0, Reason = "desconexion" }],
        };
        _h.Journal.Save(doc, DateTime.UtcNow);
        await _recovery.RunAsync();
        var back = _h.Doc();
        Assert.Equal("interrupted", back.State);
        Assert.Equal("desconexion", Assert.Single(back.Gaps).Reason);
    }

    [FfmpegFact]
    public async Task ProcesoHuerfano_SeCierraLimpio()
    {
        var seg = Open(1);
        var staging = Staging(seg);
        Directory.CreateDirectory(Path.GetDirectoryName(staging)!);
        // Un ffmpeg "grabando" en tiempo real, como el scrcpy que sobrevive a Tatana (§4.2.6).
        var psi = new ProcessStartInfo
        {
            FileName = Ffmpeg!, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true,
        };
        foreach (var a in new[] { "-hide_banner", "-nostats", "-y", "-re", "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=10",
                     "-t", "60", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", staging })
            psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi)!;
        proc.BeginErrorReadLine();
        proc.BeginOutputReadLine();
        try
        {
            await Task.Delay(2500);
            seg.Process = new SegmentProcess { Pid = proc.Id, StartedAt = proc.StartTime.ToUniversalTime(), Exe = "ffmpeg" };
            Journal(seg);

            await _recovery.RunAsync();
            Assert.True(proc.HasExited);
            var s = _h.Doc().Segments[0];
            Assert.Equal("saved", s.State);
            Assert.True(s.DurationSeconds >= 1.5, $"duración {s.DurationSeconds}");
        }
        finally
        {
            if (!proc.HasExited) proc.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public void PidDeOtroProceso_NoSeToca()
    {
        using var self = Process.GetCurrentProcess();
        var info = new SegmentProcess { Pid = self.Id, StartedAt = self.StartTime.ToUniversalTime(), Exe = "scrcpy" };
        Assert.Null(SessionRecovery.FindOwnProcess(info, SessionRecovery.ExpectedNames("scrcpy")));

        var now = DateTime.UtcNow;
        var same = new SegmentProcess { Pid = 1, StartedAt = now, Exe = "scrcpy" };
        Assert.True(SessionRecovery.IsSameProcess("scrcpy", now.AddSeconds(1.5), same, ["scrcpy"]));
        Assert.False(SessionRecovery.IsSameProcess("scrcpy", now.AddSeconds(3), same, ["scrcpy"]));
        Assert.False(SessionRecovery.IsSameProcess("dotnet", now, same, ["scrcpy"]));
        Assert.Null(SessionRecovery.FindOwnProcess(new SegmentProcess { Pid = int.MaxValue - 7, StartedAt = now }, ["scrcpy"]));
    }

    [Fact]
    public async Task RestorePendiente_SinCelular_SeRegistra()
    {
        var phoneCalls = new List<string>();
        var keeper = new StayAwakeKeeper(true, null, (args, _) =>
        {
            phoneCalls.Add(args[^1]);
            return Task.FromResult(new ProcessResult(0, "7", ""));
        });
        var recovery = new SessionRecovery(_h.Journal, _h.Closer, keeper, _h.Recorder, _h.Recovery, _h.Sink,
            new RecordingOptions().Normalize(null), TimeProvider.System, null, false,
            isConnected: _ => Task.FromResult(false), findProcess: null);
        var doc = new SessionJournalDoc
        {
            SessionId = Guid.NewGuid().ToString("N"), CaseId = SessionHarness.CaseId, Serial = SessionHarness.Serial,
            State = SessionStates.Recording,
            StayAwake = new StayAwakeState { Serial = SessionHarness.Serial, Original = "0", Owned = true },
        };
        _h.Journal.Save(doc, DateTime.UtcNow);
        await recovery.RunAsync();
        Assert.True(_h.Doc().StayAwake!.RestorePending);
        Assert.Empty(phoneCalls);
    }
}

/// <summary>
/// §4.5: un MKV con <c>flush_packets=1:cluster_time_limit=2000</c> crece en disco de forma continua; tras un kill a los
/// ~4 s, el remux recupera al menos 1 s.
/// </summary>
public sealed class MkvTruncationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "factum-agent-mkvtrunc-" + Guid.NewGuid().ToString("N"));

    public MkvTruncationTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [FfmpegFact]
    public async Task KillALos4Segundos_RemuxRecuperaAlMenosUnSegundo()
    {
        var ffmpeg = FfmpegFactAttribute.Ffmpeg!;
        var path = Path.Combine(_root, "camara.mkv");
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true,
        };
        foreach (var a in new[] { "-hide_banner", "-nostats", "-y", "-re", "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=15",
                     "-t", "60", "-c:v", "libx264", "-preset", "veryfast", "-pix_fmt", "yuv420p",
                     "-flush_packets", "1", "-cluster_time_limit", "2000", "-f", "matroska", path })
            psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi)!;
        proc.BeginErrorReadLine();
        proc.BeginOutputReadLine();
        await Task.Delay(4000);
        proc.Kill(entireProcessTree: true);
        await proc.WaitForExitAsync();

        Assert.True(await AdbService.RemuxFileAsync(path, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            CancellationToken.None));
        var probe = await FfmpegProbe.ProbeAsync(ffmpeg, path, CancellationToken.None);
        Assert.True(probe.HasVideo);
        Assert.True(probe.DurationSeconds >= 1, $"duración {probe.DurationSeconds}");
    }
}
