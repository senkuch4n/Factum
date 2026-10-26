using Factum.Agent.Models;
using Factum.Agent.Services;
using Factum.Agent.Services.DeviceDetails;
using Factum.Agent.WebSockets;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Factum.Agent.Tests.Extraction;

internal sealed class FakeEventSink : IAgentEventSink
{
    public List<AgentEvent> Events { get; } = [];
    public Task BroadcastAsync(AgentEvent evt, CancellationToken ct = default)
    {
        lock (Events) Events.Add(evt);
        return Task.CompletedTask;
    }
    public IReadOnlyList<AgentEvent> Snapshot() { lock (Events) return Events.ToList(); }
}

internal sealed class FakeDiskProbe(long free) : IAgentDiskProbe
{
    public long? GetAvailableFreeBytes(string path) => free;
}

internal sealed class FakeFolderReveal : IFolderReveal
{
    public string? Revealed { get; private set; }
    public void Reveal(string filePath) => Revealed = filePath;
}

internal sealed class FakeHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Production;
    public string ApplicationName { get; set; } = "Factum.Agent.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

/// <summary>Fuente de grabaciones activas falsa (§5.7 device_busy).</summary>
internal sealed class FakeRecordingSource : IRecordingStateSource
{
    public List<RecordingStateDto> Active { get; } = [];
    public IEnumerable<RecordingStateDto> ActiveRecordings => Active;
    public void Recording(string serial) => Active.Add(new RecordingStateDto("android", serial, "recording",
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, null, null));
}

/// <summary>Probe de pantalla falso (no se usa en mock salvo el preflight).</summary>
internal sealed class FakeScreenProbe : IAndroidScreenProbe
{
    public Task<ScreenProbeResult> GetStateAsync(string serial, CancellationToken ct) =>
        Task.FromResult(new ScreenProbeResult(ScreenState.On, null));
}

/// <summary>No se usa en modo mock; solo para construir el servicio.</summary>
internal sealed class StubDeviceDetails : IDeviceDetailsService
{
    public Task<DeviceDetailsResponse> ReadAsync(string serial, string platform, CancellationToken ct) =>
        throw new NotSupportedException();
    public Task<DeviceClockResponse> MeasureClockAsync(string serial, string platform, CancellationToken ct) =>
        throw new NotSupportedException();
}
