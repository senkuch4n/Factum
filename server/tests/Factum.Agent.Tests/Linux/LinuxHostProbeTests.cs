using System.Text.Json;
using Factum.Agent.Common;
using Factum.Agent.Services.Linux;

namespace Factum.Agent.Tests.Linux;

/// <summary>tatana-linux §5.2: cada fila de los chequeos con un árbol falso (nunca mira el sistema real).</summary>
public sealed class LinuxHostProbeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "factum-linux-" + Guid.NewGuid().ToString("N"));
    private string? _uxplay;
    private UxplayInfo? _uxplayInfo;
    private readonly HashSet<string> _noAccess = new(StringComparer.Ordinal);
    private string? _runtimeDir = "/run/user/1000";

    public LinuxHostProbeTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private void Touch(string absolute, string content = "")
    {
        var p = Path.Combine(_root, absolute.TrimStart('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
    }

    private LinuxHostProbe Probe(bool isLinux = true) => new(new LinuxHostEnvironment
    {
        Root = _root,
        GetEnv = k => k == "XDG_RUNTIME_DIR" ? _runtimeDir : null,
        Glibc = () => "2.39",
        CanReadWrite = p => !_noAccess.Contains(Path.GetFileName(p)),
        UxplayPath = () => _uxplay,
        Uxplay = _ => _uxplayInfo,
    }, isLinux);

    private LinuxCheck Check(string id) => Probe().Probe().Checks.Single(c => c.Id == id);

    [Fact]
    public void Fuera_de_Linux_es_null()
    {
        Assert.Null(Probe(isLinux: false).GetStatus());
    }

    [Fact]
    public void Siete_chequeos_en_orden_y_required_solo_usbmuxd_y_android()
    {
        var s = Probe().Probe();
        Assert.Equal(LinuxHostProbe.CheckIds, s.Checks.Select(c => c.Id).ToList());
        Assert.Equal(["usbmuxd", "android_usb"], s.Checks.Where(c => c.Required).Select(c => c.Id).ToList());
    }

    [Fact]
    public void Distro_glibc_y_gestor()
    {
        Touch("/etc/os-release", "ID=ubuntu\nVERSION_ID=\"24.04\"\nPRETTY_NAME=\"Ubuntu 24.04.1 LTS\"\nID_LIKE=debian\n");
        Touch("/usr/bin/apt-get");
        var s = Probe().Probe();
        Assert.Equal("ubuntu", s.DistroId);
        Assert.Equal("24.04", s.DistroVersion);
        Assert.Equal("Ubuntu 24.04.1 LTS", s.DistroName);
        Assert.Equal("debian", s.DistroLike);
        Assert.Equal("2.39", s.GlibcVersion);
        Assert.Equal("apt", s.PackageManager);
        Assert.True(s.SetupSupported);
    }

    [Theory]
    [InlineData("/usr/bin/apt-get", "apt", true)]
    [InlineData("/usr/bin/dnf", "dnf", true)]
    [InlineData("/usr/bin/pacman", "pacman", false)]
    [InlineData("/usr/bin/zypper", "zypper", false)]
    [InlineData(null, "unknown", false)]
    public void Gestor_de_paquetes(string? bin, string expected, bool supported)
    {
        if (bin is not null) Touch(bin);
        var s = Probe().Probe();
        Assert.Equal(expected, s.PackageManager);
        Assert.Equal(supported, s.SetupSupported);
    }

    [Fact]
    public void Usbmuxd_socket_instalado_o_nada()
    {
        Assert.Equal("missing", Check("usbmuxd").Status);
        Touch("/lib/systemd/system/usbmuxd.service");
        Assert.Equal("ok", Check("usbmuxd").Status);
        Assert.NotNull(Check("usbmuxd").Detail);
        Touch("/var/run/usbmuxd");
        Assert.Equal(new LinuxCheck("usbmuxd", "ok", true), Check("usbmuxd"));
    }

    [Theory]
    [InlineData("/etc/udev/rules.d/70-factum-tatana.rules")]
    [InlineData("/etc/udev/rules.d/51-android.rules")]
    [InlineData("/usr/lib/udev/rules.d/51-android.rules")]
    [InlineData("/lib/udev/rules.d/51-android.rules")]
    public void Android_usb_con_alguna_regla(string rule)
    {
        Assert.Equal("missing", Check("android_usb").Status);
        Touch(rule);
        Assert.Equal("ok", Check("android_usb").Status);
    }

    [Fact]
    public void Avahi_corriendo_instalado_o_nada()
    {
        Assert.Equal("missing", Check("avahi").Status);
        Touch("/usr/sbin/avahi-daemon");
        Assert.Equal("warning", Check("avahi").Status);
        Touch("/run/avahi-daemon/socket");
        Assert.Equal("ok", Check("avahi").Status);
    }

    [Fact]
    public void Uxplay_falta_viejo_o_con_mp4()
    {
        Assert.Equal("missing", Check("uxplay").Status);
        _uxplay = "/usr/bin/uxplay";
        _uxplayInfo = new UxplayInfo(false, "1.68");
        Assert.Equal(new LinuxCheck("uxplay", "outdated", false, "UxPlay 1.68"), Check("uxplay"));
        _uxplayInfo = new UxplayInfo(false, null);
        Assert.Equal("outdated", Check("uxplay").Status);
        _uxplayInfo = new UxplayInfo(true, "1.73");
        Assert.Equal("ok", Check("uxplay").Status);
    }

    [Fact]
    public void Camara_sin_nodos_con_permiso_o_sin_permiso()
    {
        Assert.Equal("not_applicable", Check("camera").Status);
        Touch("/dev/video0");
        Touch("/sys/class/video4linux/video0/index", "0\n");
        Touch("/dev/video1");
        Touch("/sys/class/video4linux/video1/index", "1\n");
        Assert.Equal("ok", Check("camera").Status);
        _noAccess.Add("video1"); // el de metadatos no cuenta
        Assert.Equal("ok", Check("camera").Status);
        _noAccess.Add("video0");
        Assert.Equal(new LinuxCheck("camera", "warning", false, "/dev/video0"), Check("camera"));
    }

    [Fact]
    public void Microfono_por_el_socket_de_pulse()
    {
        Assert.Equal("warning", Check("microphone").Status);
        Assert.Equal("No hay servidor de sonido PulseAudio/PipeWire en esta sesión", Check("microphone").Detail);
        Touch("/run/user/1000/pulse/native");
        Assert.Equal("ok", Check("microphone").Status);
        _runtimeDir = null;
        Assert.Equal("warning", Check("microphone").Status);
    }

    [Fact]
    public void Sandbox_segun_sysctl_y_perfil()
    {
        Assert.Equal("not_applicable", Check("sandbox").Status);
        Touch("/proc/sys/kernel/apparmor_restrict_unprivileged_userns", "0\n");
        Assert.Equal("not_applicable", Check("sandbox").Status);
        Touch("/proc/sys/kernel/apparmor_restrict_unprivileged_userns", "1\n");
        Assert.Equal("warning", Check("sandbox").Status);
        Touch("/etc/apparmor.d/factum-tatana");
        Assert.Equal("ok", Check("sandbox").Status);
    }

    [Fact]
    public void Setup_state_ok_solo_con_los_requeridos()
    {
        Assert.Equal("incomplete", Probe().Probe().SetupState);
        Touch("/usr/sbin/usbmuxd");
        Assert.Equal("incomplete", Probe().Probe().SetupState);
        Touch("/etc/udev/rules.d/70-factum-tatana.rules");
        Assert.Equal("ok", Probe().Probe().SetupState); // avahi, uxplay, etc. no son requeridos
    }

    [Fact]
    public void Cachea_5_segundos()
    {
        var now = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        var probe = new LinuxHostProbe(new LinuxHostEnvironment
        {
            Root = _root, GetEnv = _ => null, Glibc = () => "2.35", CanReadWrite = _ => true,
            UxplayPath = () => null, Uxplay = _ => null,
        }, isLinux: true, () => now);
        Assert.Equal("incomplete", probe.GetStatus()!.SetupState);
        Touch("/usr/sbin/usbmuxd");
        Touch("/etc/udev/rules.d/70-factum-tatana.rules");
        now = now.AddSeconds(4);
        Assert.Equal("incomplete", probe.GetStatus()!.SetupState);
        now = now.AddSeconds(2);
        Assert.Equal("ok", probe.GetStatus()!.SetupState);
    }

    // §10.A: nombres JSON exactos con la política del agente (snake_case_lower, sin nulls).
    [Fact]
    public void Contrato_JSON_de_linux()
    {
        Touch("/etc/os-release", "ID=ubuntu\nVERSION_ID=24.04\n");
        var json = JsonSerializer.Serialize(Probe().Probe(), LinuxSelfTest.JsonOptions);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        foreach (var key in new[] { "distro_id", "distro_version", "glibc_version", "package_manager", "setup_supported",
                     "setup_state", "checks" })
            Assert.True(root.TryGetProperty(key, out _), key);
        Assert.False(root.TryGetProperty("distro_name", out _)); // null no viaja
        var check = root.GetProperty("checks")[0];
        Assert.Equal("usbmuxd", check.GetProperty("id").GetString());
        Assert.Equal("missing", check.GetProperty("status").GetString());
        Assert.True(check.GetProperty("required").GetBoolean());
        Assert.True(check.TryGetProperty("detail", out _));
    }

    [Theory]
    [InlineData("2.39", "2.35", true)]
    [InlineData("2.35", "2.35", true)]
    [InlineData("2.31", "2.35", false)]
    [InlineData("2.4", "2.35", false)]
    [InlineData(null, "2.35", false)]
    [InlineData("basura", "2.35", false)]
    public void Version_minima(string? v, string min, bool expected) =>
        Assert.Equal(expected, LinuxHostProbe.VersionAtLeast(v, min));

    [Fact]
    public void Os_de_health()
    {
        Assert.Equal("windows", HostOsInfo.JsonName(HostOs.Windows));
        Assert.Equal("macos", HostOsInfo.JsonName(HostOs.MacOS));
        Assert.Equal("linux", HostOsInfo.JsonName(HostOs.Linux));
        var expected = OperatingSystem.IsWindows() ? HostOs.Windows : OperatingSystem.IsMacOS() ? HostOs.MacOS : HostOs.Linux;
        Assert.Equal(expected, HostOsInfo.Current);
    }

    [Fact]
    public void Linux_de_health_es_null_fuera_de_Linux_y_no_viaja()
    {
        var probe = new LinuxHostProbe();
        if (!OperatingSystem.IsLinux())
        {
            Assert.Null(probe.GetStatus());
            var json = JsonSerializer.Serialize(new { os = HostOsInfo.JsonName(HostOsInfo.Current), linux = probe.GetStatus() },
                LinuxSelfTest.JsonOptions);
            Assert.DoesNotContain("\"linux\"", json);
        }
        else
        {
            Assert.NotNull(probe.GetStatus());
        }
    }
}
