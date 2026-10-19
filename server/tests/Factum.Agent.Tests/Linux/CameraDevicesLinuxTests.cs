using Factum.Agent.Services.Session;

namespace Factum.Agent.Tests.Linux;

/// <summary>tatana-linux D-T15: una cámara UVC expone video0 (captura, index 0) y video1 (metadatos, index 1).</summary>
public sealed class CameraDevicesLinuxTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "factum-v4l2-" + Guid.NewGuid().ToString("N"));
    private string Dev => Path.Combine(_root, "dev");
    private string Sys => Path.Combine(_root, "sys");

    public CameraDevicesLinuxTests()
    {
        Directory.CreateDirectory(Dev);
        Directory.CreateDirectory(Sys);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private void Node(string name, string? index, string? label = null)
    {
        File.WriteAllText(Path.Combine(Dev, name), "");
        var sys = Path.Combine(Sys, name);
        Directory.CreateDirectory(sys);
        if (index is not null) File.WriteAllText(Path.Combine(sys, "index"), index + "\n");
        if (label is not null) File.WriteAllText(Path.Combine(sys, "name"), label + "\n");
    }

    [Fact]
    public void Dos_nodos_index_0_y_1_son_una_sola_camara()
    {
        Node("video0", "0", "HD Webcam");
        Node("video1", "1", "HD Webcam");
        var list = CameraDevices.ListLinux(Dev, Sys);
        var cam = Assert.Single(list);
        Assert.Equal(Path.Combine(Dev, "video0"), cam.Id);
        Assert.Equal("HD Webcam", cam.Name);
        Assert.Equal(CameraArgs.BackendLinux, cam.Backend);
    }

    [Fact]
    public void Sin_index_se_incluye_y_se_ignoran_nombres_raros()
    {
        Node("video0", "0");
        Node("video1", "1");
        Node("video2", null);
        File.WriteAllText(Path.Combine(Dev, "video-loopback"), "");
        var nodes = CameraDevices.CaptureNodes(Dev, Sys).Select(Path.GetFileName).ToList();
        Assert.Equal(["video0", "video2"], nodes);
    }

    [Fact]
    public void Sin_dev_es_lista_vacia()
    {
        Assert.Empty(CameraDevices.ListLinux(Path.Combine(_root, "no-existe"), Sys));
    }
}
