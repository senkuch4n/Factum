using System.Collections.Concurrent;
using Factum.Agent.Common;
using Factum.Agent.Models;
using Factum.Agent.Services;
using Factum.Agent.Services.Session;
using Factum.Agent.WebSockets;

namespace Factum.Agent.Tests.Session;

/// <summary>Sink de eventos que guarda todo (orden de emisión).</summary>
public sealed class RecordingSink : IAgentEventSink
{
    public ConcurrentQueue<AgentEvent> Events { get; } = new();

    public Task BroadcastAsync(AgentEvent evt, CancellationToken ct = default)
    {
        Events.Enqueue(evt);
        return Task.CompletedTask;
    }

    public List<AgentEvent> OfType(string type) => Events.Where(e => e.Type == type).ToList();
}

/// <summary>Pantalla siempre en el estado pedido.</summary>
public sealed class FixedScreenProbe(ScreenState state = ScreenState.On) : IAndroidScreenProbe
{
    public ScreenState State { get; set; } = state;

    public Task<ScreenProbeResult> GetStateAsync(string serial, CancellationToken ct) =>
        Task.FromResult(new ScreenProbeResult(State, null));
}

public sealed class FixedDiskProbe(long? free) : IAgentDiskProbe
{
    public long? Free { get; set; } = free;
    public long? GetAvailableFreeBytes(string path) => Free;
}

/// <summary>
/// Pista falsa: cada tramo "grabado" es el MKV embebido de 1 s (válido para ffmpeg). Se le puede pedir que falle el
/// próximo arranque o corte, o que la captura termine sola.
/// </summary>
public sealed class FakeTrack : ISessionTrack
{
    private readonly FakeTrackFactory _owner;
    public string? CurrentPath { get; private set; }
    public event Action<TrackExit>? Exited;

    public FakeTrack(FakeTrackFactory owner) => _owner = owner;

    public Task<TrackStart> StartAsync(string outputPath)
    {
        if (_owner.FailNextStart)
        {
            _owner.FailNextStart = false;
            throw new AgentHttpException(504, AgentErrorCodes.RecordingStartTimeout, "El celular no empezó a grabar a tiempo.");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        CurrentPath = outputPath;
        _owner.Started.Add(outputPath);
        return Task.FromResult(new TrackStart(DateTime.UtcNow, null, null));
    }

    public async Task<(TrackStart Next, TrackStop Previous)> RotateAsync(string nextPath, Func<TrackStart, Task> onNextStarted)
    {
        var prev = CurrentPath ?? throw new InvalidOperationException("sin captura");
        if (_owner.FailNextRotate)
        {
            _owner.FailNextRotate = false;
            throw new AgentHttpException(504, AgentErrorCodes.RecordingStartTimeout, "El celular no empezó a grabar a tiempo.");
        }
        var next = new TrackStart(DateTime.UtcNow, null, null);
        CurrentPath = nextPath;
        _owner.Started.Add(nextPath);
        await onNextStarted(next);
        await Task.Delay(20);
        var requested = DateTime.UtcNow;
        Write(prev);
        return (next, new TrackStop(requested, true));
    }

    public Task<TrackStop> StopAsync()
    {
        var path = CurrentPath;
        CurrentPath = null;
        var requested = DateTime.UtcNow;
        if (path is not null) Write(path);
        return Task.FromResult(new TrackStop(requested, true));
    }

    /// <summary>La captura termina sola (desconexión o error).</summary>
    public void RaiseExit()
    {
        var path = CurrentPath ?? throw new InvalidOperationException("sin captura");
        Write(path);
        Exited?.Invoke(new TrackExit(path, DateTime.UtcNow));
    }

    private void Write(string path)
    {
        if (_owner.EmptyFiles) File.WriteAllBytes(path, []);
        else File.WriteAllBytes(path, AdbService.MockMkvBytes());
    }

    public void Dispose() { }
}

public sealed class FakeTrackFactory : ISessionTrackFactory
{
    public bool FailNextStart { get; set; }
    public bool FailNextRotate { get; set; }
    public bool EmptyFiles { get; set; }
    public List<string> Started { get; } = [];
    public List<FakeTrack> Tracks { get; } = [];
    public FakeTrack Last => Tracks[^1];

    public ISessionTrack CreateScreen(string serial, int androidVersion)
    {
        var t = new FakeTrack(this);
        Tracks.Add(t);
        return t;
    }
}

/// <summary>
/// Cámara falsa (F2): no lanza ffmpeg. Cada tramo es el MKV embebido de 1 s en su <c>part_file</c>; el test decide cuándo
/// "rota" (línea del CSV), cuándo falla y si el cierre deja la última línea o hubo que matarla.
/// </summary>
public sealed class FakeCameraTrack(FakeCameraTrackFactory owner, CameraSpec spec) : ICameraTrack
{
    public CameraSpec Spec { get; } = spec;
    public string? Staging { get; private set; }
    public int Current { get; private set; }
    public DateTime Anchor { get; private set; }
    public bool Stopped { get; private set; }
    private double _segStart;
    public event Action<CameraSegmentClosed>? SegmentClosed;
    public event Action<CameraExit>? Exited;

    public Task<CameraStart> StartAsync(string stagingDir, int startNumber, int segmentSeconds)
    {
        if (owner.FailNextStart is { } fail)
        {
            owner.FailNextStart = null;
            throw new CameraStartException(fail == "" ? null : fail, "La cámara no abrió");
        }
        Staging = stagingDir;
        Current = startNumber;
        Anchor = DateTime.UtcNow;
        _segStart = 0;
        owner.StartNumbers.Add(startNumber);
        owner.SegmentSeconds = segmentSeconds;
        Write(Current);
        return Task.FromResult(new CameraStart(Anchor, 4242, Anchor, 1280, 720, 15, false));
    }

    private double Elapsed => Math.Max(_segStart + 0.001, (DateTime.UtcNow - Anchor).TotalSeconds);

    private void Write(int index) =>
        File.WriteAllBytes(Path.Combine(Staging!, CameraArgs.PartName(index)),
            owner.EmptyFiles ? [] : AdbService.MockMkvBytes());

    /// <summary>El segment muxer cerró el tramo en curso y abrió el siguiente.</summary>
    public void Rotate()
    {
        var end = Elapsed;
        Write(Current + 1);
        var ev = new CameraSegmentClosed(Current, Path.Combine(Staging!, CameraArgs.PartName(Current)), _segStart, end);
        Current++;
        _segStart = end;
        SegmentClosed?.Invoke(ev);
    }

    /// <summary>ffmpeg terminó solo.</summary>
    public void Fail(string? detail) => Exited?.Invoke(new CameraExit(DateTime.UtcNow, detail, "fake"));

    public Task<TrackStop> StopAsync()
    {
        var requested = DateTime.UtcNow;
        Stopped = true;
        if (owner.KillOnStop) return Task.FromResult(new TrackStop(requested, false));
        SegmentClosed?.Invoke(new CameraSegmentClosed(Current, Path.Combine(Staging!, CameraArgs.PartName(Current)),
            _segStart, Elapsed, Final: true));
        return Task.FromResult(new TrackStop(requested, true));
    }

    public void Dispose() { }
}

public sealed class FakeCameraTrackFactory : ICameraTrackFactory
{
    /// <summary>"" = falla sin detalle; null = arranca.</summary>
    public string? FailNextStart { get; set; }
    public bool KillOnStop { get; set; }
    public bool EmptyFiles { get; set; }
    public int SegmentSeconds { get; set; }
    public List<int> StartNumbers { get; } = [];
    public List<FakeCameraTrack> Tracks { get; } = [];
    public FakeCameraTrack Last => Tracks[^1];

    public ICameraTrack Create(CameraSpec spec)
    {
        var t = new FakeCameraTrack(this, spec);
        Tracks.Add(t);
        return t;
    }
}

/// <summary>Vista previa falsa: cuenta los cortes.</summary>
public sealed class FakePreview : ICameraPreview
{
    public int Stops;
    public Task StopAsync()
    {
        Interlocked.Increment(ref Stops);
        return Task.CompletedTask;
    }
}

/// <summary>Entorno de una sesión con dobles: carpetas temporales propias que se borran al final.</summary>
public sealed class SessionHarness : IDisposable
{
    public const string CaseId = "3f2a9c1e-0000-4000-8000-0000000000c1";
    public const string OtherCaseId = "3f2a9c1e-0000-4000-8000-0000000000c2";
    public const string Serial = "R58RA3R5AGA";

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "factum-agent-session-" + Guid.NewGuid().ToString("N"));
    public AgentOptions Options { get; }
    public SessionJournal Journal { get; }
    public CaseEvidenceStore Store { get; }
    public SegmentCloser Closer { get; }
    public FakeTrackFactory Tracks { get; } = new();
    public FakeCameraTrackFactory Cameras { get; } = new();
    public FakePreview Preview { get; } = new();
    public FixedScreenProbe Screen { get; } = new();
    public FixedDiskProbe Disk { get; } = new(500L * 1024 * 1024 * 1024);
    public RecordingSink Sink { get; } = new();
    public SessionRecoveryStatus Recovery { get; } = new();
    public HashSet<string> Connected { get; } = new(StringComparer.Ordinal) { Serial };
    public List<IRecordingStateSource> PointRecordings { get; } = [];
    public SessionRecorder Recorder { get; }

    public SessionHarness(Action<SessionOptions>? configure = null)
    {
        Options = new AgentOptions
        {
            DataDirectory = Path.Combine(Root, "agent-data"),
            EvidenceDirectory = Path.Combine(Root, "Evidencia"),
            MinFreeBytes = 1024L * 1024 * 1024,
        };
        Options.Session.ResumeDelaySeconds = 0;
        Options.Session.ScreenProbeSeconds = 0;
        Options.Session.CameraEnabled = true;
        configure?.Invoke(Options.Session);
        Journal = new SessionJournal(Options.DataDirectory);
        Store = new CaseEvidenceStore(Options, Disk, sessions: Journal);
        Closer = new SegmentCloser(Journal, Store, new RecordingOptions().Normalize(null));
        Recorder = new SessionRecorder(Options, Journal, Closer,
            new StayAwakeKeeper(false, null, (_, _) => Task.FromResult(new ProcessResult(0, "0", ""))),
            Tracks, Screen, Disk, Sink, PointRecordings, Recovery, TimeProvider.System, null,
            serial => Task.FromResult(Connected.Contains(serial)), Cameras, Preview);
    }

    public StartSessionRequest Start(string serial = Serial, bool skip = false) =>
        new(serial, "android", 11, 30, skip, "t_1");

    public static readonly StartCameraRequest Mac = new("Cámara de la MacBook Air", "Cámara de la MacBook Air", false, "t_c");

    public StartSessionRequest StartWithCamera(StartCameraRequest? camera = null) =>
        new(Serial, "android", 11, 30, false, "t_1", Camera: camera ?? Mac);

    public SessionJournalDoc Doc(string caseId = CaseId) => Journal.Load(caseId).Doc!;

    public static async Task Eventually(Func<bool> condition, int timeoutMs = 10000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("La condición no se cumplió a tiempo");
            await Task.Delay(25);
        }
    }

    public void Dispose()
    {
        Recorder.Dispose();
        try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
    }
}
