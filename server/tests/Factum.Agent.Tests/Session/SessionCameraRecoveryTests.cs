using System.Diagnostics;
using Factum.Agent.Common;
using Factum.Agent.Models;
using Factum.Agent.Services;
using Factum.Agent.Services.Session;

namespace Factum.Agent.Tests.Session;

/// <summary>
/// Recuperación de la cámara (grabacion-sesion-completa-f2 §5.8, §14.1): ffmpeg lavfi lanzado por el test (como si
/// Tatana hubiera muerto con la cámara grabando). Nunca abre una cámara; cada ffmpeg se cierra por su PID.
/// </summary>
public sealed class SessionCameraRecoveryTests : IDisposable
{
    private readonly SessionHarness _h = new();
    private readonly SessionRecovery _recovery;
    private readonly List<Process> _procs = [];
    private const string Case = SessionHarness.CaseId;

    public SessionCameraRecoveryTests()
    {
        _recovery = new SessionRecovery(_h.Journal, _h.Closer,
            new StayAwakeKeeper(false, null, (_, _) => Task.FromResult(new ProcessResult(0, "0", ""))),
            _h.Recorder, _h.Recovery, _h.Sink, new RecordingOptions().Normalize(null), TimeProvider.System, null,
            mock: false, isConnected: s => Task.FromResult(_h.Connected.Contains(s)), findProcess: null);
    }

    public void Dispose()
    {
        foreach (var p in _procs)
        {
            try
            {
                if (!p.HasExited)
                {
                    p.Kill();
                    p.WaitForExit(5000);
                }
            }
            catch { /* ya terminó */ }
            p.Dispose();
        }
        _h.Dispose();
    }

    private string Dir => _h.Journal.DirFor(Case);

    private Process Launch(int segmentSeconds)
    {
        Directory.CreateDirectory(Dir);
        var args = CameraArgs.Build(new CameraArgsInput("lavfi", "", false, null, null, 320, 240, 10, 30, segmentSeconds, 1,
            Dir));
        var psi = new ProcessStartInfo
        {
            FileName = FfmpegFactAttribute.Ffmpeg!, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        var p = Process.Start(psi)!;
        _procs.Add(p);
        p.OutputDataReceived += (_, _) => { };
        p.ErrorDataReceived += (_, _) => { };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return p;
    }

    private SessionJournalDoc Journal(Process p, DateTime anchor)
    {
        var run = new CameraRunState
        {
            Id = "mock-cam", Name = "Cámara simulada", Width = 320, Height = 180, Fps = 10, AnchorAt = anchor,
            StartNumber = 1, Pid = p.Id, ProcessStartedAt = p.StartTime.ToUniversalTime(),
        };
        var doc = new SessionJournalDoc
        {
            SessionId = Guid.NewGuid().ToString("N"), CaseId = Case, Serial = SessionHarness.Serial, SegmentMinutes = 30,
            State = SessionStates.WaitingDevice, CameraRun = run, LastAliveAt = anchor.AddSeconds(1),
            Camera = new SessionCameraDto("Cámara simulada", 320, 180, 10, false),
            Segments =
            [
                CameraJournal.NewSegment(1, anchor, new SessionCameraDto("Cámara simulada", 320, 180, 10, false), run,
                    TimeZoneInfo.Local),
            ],
        };
        _h.Journal.Save(doc, DateTime.UtcNow);
        return doc;
    }

    private int CsvLines() => CameraCsv.Read(Path.Combine(Dir, CameraArgs.CsvFileName)).Count;

    private List<JournalSegment> Camera() =>
        _h.Doc().Segments.Where(s => s.Track == "camara").OrderBy(s => s.Index).ToList();

    [FfmpegFact]
    public async Task Huerfano_CerroTramosSolo_SigtermConciliaTodo()
    {
        var anchor = DateTime.UtcNow;
        var p = Launch(segmentSeconds: 2);
        Journal(p, anchor);
        // Tatana "murió": EOF en stdin. ffmpeg sigue grabando y cierra tramos solo (§4.2).
        p.StandardInput.Close();
        await Task.Delay(1500);
        Assert.False(p.HasExited);
        await SessionHarness.Eventually(() => CsvLines() >= 2, 20000);
        var ajeno = Path.Combine(Dir, "camara_0099.part.mkv");
        await File.WriteAllBytesAsync(ajeno, [9, 9, 9]);

        Assert.True(await _recovery.RunAsync() >= 3);
        Assert.True(p.HasExited); // SIGTERM: cierre limpio
        var cams = Camera();
        Assert.True(cams.Count >= 3, $"tramos: {cams.Count}");
        Assert.All(cams, s => Assert.Equal("saved", s.State));
        Assert.All(cams, s => Assert.True(s.Recovered));
        Assert.Equal(Enumerable.Range(1, cams.Count), cams.Select(s => s.Index));
        for (var i = 1; i < cams.Count; i++) Assert.Equal(cams[i - 1].StoppedAt, cams[i].StartedAt);
        Assert.All(cams[..^1], s => Assert.Equal("rotacion", s.StopReason));
        Assert.Equal("tatana_cerrado", cams[^1].StopReason);
        Assert.All(cams, s => Assert.True(File.Exists(Path.Combine(_h.Store.CaseDir(Case), s.Filename))));
        Assert.All(cams, s => Assert.Null(s.PartFile));
        var doc = _h.Doc();
        Assert.Null(doc.CameraRun);
        Assert.Equal("tatana_cerrado", Assert.Single(doc.Gaps, g => g.Track == "camara").Reason);
        Assert.Equal("interrupted", doc.State);
        Assert.False(File.Exists(Path.Combine(Dir, CameraArgs.CsvFileName)));
        Assert.Equal([9, 9, 9], await File.ReadAllBytesAsync(ajeno)); // part_file ajeno: no se toca
    }

    [FfmpegFact]
    public async Task Kill9_ElTramoSinLineaSeRemuxea()
    {
        var anchor = DateTime.UtcNow;
        var p = Launch(segmentSeconds: 6);
        Journal(p, anchor);
        await SessionHarness.Eventually(() => CsvLines() >= 1, 20000);
        await Task.Delay(4500);
        p.Kill();
        await p.WaitForExitAsync();

        await _recovery.RunAsync();
        var cams = Camera();
        Assert.Equal(2, cams.Count);
        Assert.Equal("saved", cams[0].State);
        Assert.Equal("saved", cams[1].State);
        Assert.True(cams[1].DurationSeconds >= 1, $"duración {cams[1].DurationSeconds}");
        Assert.Equal("tatana_cerrado", cams[1].StopReason);
        Assert.Null(_h.Doc().CameraRun);
    }

    [Fact]
    public async Task PidDeOtroProceso_NoSeToca()
    {
        using var self = Process.GetCurrentProcess();
        var run = new CameraRunState
        {
            Id = "mock-cam", Name = "Cámara simulada", Width = 320, Height = 180, Fps = 10, AnchorAt = DateTime.UtcNow,
            StartNumber = 1, Pid = self.Id, ProcessStartedAt = self.StartTime.ToUniversalTime(),
        };
        var doc = new SessionJournalDoc
        {
            SessionId = Guid.NewGuid().ToString("N"), CaseId = Case, Serial = SessionHarness.Serial, SegmentMinutes = 30,
            State = SessionStates.Recording, CameraRun = run,
            Segments = [CameraJournal.NewSegment(1, run.AnchorAt, new SessionCameraDto("x", 320, 180, 10, false), run, TimeZoneInfo.Local)],
        };
        _h.Journal.Save(doc, DateTime.UtcNow);
        await _recovery.RunAsync(); // el proceso de los tests no se llama ffmpeg: no se le manda nada
        Assert.False(self.HasExited);
        var after = _h.Doc();
        Assert.Equal("discarded", Camera()[0].State); // sin archivo: descartado
        Assert.True(Assert.Single(after.Gaps, g => g.Track == "camara").SegmentLost);
    }
}
