using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Factum.Agent.Common;
using Factum.Agent.Services.Ios;
using Factum.Agent.Services.Session;

namespace Factum.Agent.Services.Linux;

/// <summary>Un chequeo de "Esta PC" (§10.A, <c>linux.checks[]</c>). <see cref="Status"/> es string a propósito.</summary>
public sealed record LinuxCheck(string Id, string Status, bool Required, string? Detail = null);

/// <summary>Objeto <c>linux</c> de <c>/health</c> (SDD tatana-linux §10.A). Solo existe en Linux.</summary>
public sealed record LinuxHostStatus(
    string DistroId,
    string? DistroVersion,
    string? DistroName,
    string? DistroLike,
    [property: JsonPropertyName("glibc_version")] string? GlibcVersion,
    string PackageManager,
    bool SetupSupported,
    string SetupState,
    IReadOnlyList<LinuxCheck> Checks);

/// <summary>Todo lo que <see cref="LinuxHostProbe"/> lee del sistema, inyectable para los tests (árbol falso).</summary>
public sealed class LinuxHostEnvironment
{
    /// <summary>Prefijo de las rutas absolutas ("" o "/" = el sistema real; en los tests, la raíz del árbol falso).</summary>
    public string Root { get; init; } = "/";
    public Func<string, string?> GetEnv { get; init; } = Environment.GetEnvironmentVariable;
    public Func<string?> Glibc { get; init; } = LinuxNative.GlibcVersion;
    /// <summary><c>access(path, R_OK|W_OK) == 0</c> sobre la ruta ya mapeada.</summary>
    public Func<string, bool> CanReadWrite { get; init; } = LinuxNative.CanReadWrite;
    public Func<string?> UxplayPath { get; init; } = () => ToolResolver.Find(AgentTools.Uxplay)?.Path;
    public Func<string, UxplayInfo?> Uxplay { get; init; } = UxplayProbe.Shared.Get;

    public static LinuxHostEnvironment System { get; } = new();

    /// <summary>Ruta absoluta del sistema → ruta real (con el prefijo <see cref="Root"/>).</summary>
    public string Map(string absolute) =>
        Root is "" or "/" ? absolute : Path.Combine(Root, absolute.TrimStart('/'));

    public bool Exists(string absolute)
    {
        var p = Map(absolute);
        return File.Exists(p) || Directory.Exists(p) || IsSpecialFile(p);
    }

    public string? ReadText(string absolute)
    {
        var p = Map(absolute);
        try { return File.Exists(p) ? File.ReadAllText(p) : null; }
        catch { return null; }
    }

    // Sockets (/var/run/usbmuxd, /run/avahi-daemon/socket, …/pulse/native): File.Exists da false con algunos tipos
    // especiales en Unix; se miran los atributos.
    private static bool IsSpecialFile(string p)
    {
        try { return new FileInfo(p).Exists || (File.GetAttributes(p) & FileAttributes.Device) != 0; }
        catch { return false; }
    }
}

/// <summary>
/// Estado de la PC Linux para <c>/health.linux</c> (SDD tatana-linux §5.2): distro, glibc, gestor de paquetes y los 7
/// chequeos, sin pedir contraseña. Fuera de Linux, <see cref="GetStatus"/> devuelve null. Caché de 5 s.
/// </summary>
public sealed class LinuxHostProbe
{
    internal static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(5);

    public const string UdevRulePath = "/etc/udev/rules.d/70-factum-tatana.rules";
    public const string AppArmorProfilePath = "/etc/apparmor.d/factum-tatana";
    public const string UsernsSysctlPath = "/proc/sys/kernel/apparmor_restrict_unprivileged_userns";

    /// <summary>Orden fijo de <c>linux.checks</c> (§10.A).</summary>
    public static readonly IReadOnlyList<string> CheckIds =
        ["usbmuxd", "android_usb", "avahi", "uxplay", "camera", "microphone", "sandbox"];

    private readonly LinuxHostEnvironment _env;
    private readonly bool _isLinux;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private (LinuxHostStatus Status, DateTimeOffset At)? _cached;

    public LinuxHostProbe() : this(LinuxHostEnvironment.System, OperatingSystem.IsLinux()) { }

    internal LinuxHostProbe(LinuxHostEnvironment env, bool isLinux, Func<DateTimeOffset>? clock = null)
    {
        _env = env;
        _isLinux = isLinux;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public LinuxHostStatus? GetStatus()
    {
        if (!_isLinux) return null;
        var now = _clock();
        lock (_gate)
        {
            if (_cached is { } c && now - c.At < CacheDuration) return c.Status;
        }
        LinuxHostStatus status;
        try { status = Probe(); }
        catch
        {
            status = new LinuxHostStatus("linux", null, null, null, null, "unknown", false, "incomplete", []);
        }
        lock (_gate) _cached = (status, _clock());
        return status;
    }

    internal LinuxHostStatus Probe()
    {
        var os = OsRelease.Read(_env.ReadText);
        string? glibc;
        try { glibc = _env.Glibc(); }
        catch { glibc = null; }
        var pm = PackageManager();
        var checks = new List<LinuxCheck>
        {
            Usbmuxd(), AndroidUsb(), Avahi(), Uxplay(), Camera(), Microphone(), Sandbox(),
        };
        var state = checks.Where(c => c.Required).All(c => c.Status == "ok") ? "ok" : "incomplete";
        return new LinuxHostStatus(os.Id, os.VersionId, os.PrettyName, os.IdLike, glibc, pm,
            pm is "apt" or "dnf", state, checks);
    }

    internal string PackageManager() =>
        _env.Exists("/usr/bin/apt-get") ? "apt"
        : _env.Exists("/usr/bin/dnf") ? "dnf"
        : _env.Exists("/usr/bin/pacman") ? "pacman"
        : _env.Exists("/usr/bin/zypper") ? "zypper"
        : "unknown";

    private LinuxCheck Usbmuxd()
    {
        if (_env.Exists(AppleServiceProbe.UnixSocketPath)) return new("usbmuxd", "ok", true);
        if (AppleServiceProbe.IsUsbmuxdInstalled(_env.Exists))
            return new("usbmuxd", "ok", true, "Instalado, arranca al conectar el iPhone.");
        return new("usbmuxd", "missing", true, "Falta el servicio usbmuxd para reconocer el iPhone.");
    }

    private LinuxCheck AndroidUsb()
    {
        string[] rules =
        [
            UdevRulePath, "/etc/udev/rules.d/51-android.rules", "/usr/lib/udev/rules.d/51-android.rules",
            "/lib/udev/rules.d/51-android.rules",
        ];
        return rules.Any(_env.Exists)
            ? new("android_usb", "ok", true)
            : new("android_usb", "missing", true, "Falta el permiso para usar celulares Android por USB.");
    }

    private LinuxCheck Avahi()
    {
        if (_env.Exists(AirplayRules.AvahiSocketPath)) return new("avahi", "ok", false);
        if (_env.Exists("/usr/sbin/avahi-daemon"))
            return new("avahi", "warning", false, "El servicio avahi está instalado pero no está activo.");
        return new("avahi", "missing", false, "Falta el servicio avahi.");
    }

    private LinuxCheck Uxplay()
    {
        string? path;
        try { path = _env.UxplayPath(); }
        catch { path = null; }
        if (path is null) return new("uxplay", "missing", false, "uxplay no está instalado.");
        UxplayInfo? info;
        try { info = _env.Uxplay(path); }
        catch { info = null; }
        if (info is { SupportsMp4: true }) return new("uxplay", "ok", false, info.Version is { } v ? "UxPlay " + v : null);
        return new("uxplay", "outdated", false,
            info?.Version is { } old ? "UxPlay " + old : "Esta versión de uxplay no puede grabar.");
    }

    private LinuxCheck Camera()
    {
        List<string> nodes;
        try { nodes = CameraDevices.CaptureNodes(_env.Map("/dev"), _env.Map("/sys/class/video4linux")); }
        catch { nodes = []; }
        if (nodes.Count == 0) return new("camera", "not_applicable", false);
        foreach (var n in nodes)
        {
            bool ok;
            try { ok = _env.CanReadWrite(n); }
            catch { ok = false; }
            if (!ok) return new("camera", "warning", false, "/dev/" + Path.GetFileName(n));
        }
        return new("camera", "ok", false);
    }

    private LinuxCheck Microphone()
    {
        var runtime = _env.GetEnv("XDG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(runtime) && _env.Exists(Path.Combine(runtime, "pulse", "native")))
            return new("microphone", "ok", false);
        return new("microphone", "warning", false, "No hay servidor de sonido PulseAudio/PipeWire en esta sesión");
    }

    private LinuxCheck Sandbox()
    {
        var value = _env.ReadText(UsernsSysctlPath)?.Trim();
        if (value != "1") return new("sandbox", "not_applicable", false);
        return _env.Exists(AppArmorProfilePath)
            ? new("sandbox", "ok", false)
            : new("sandbox", "warning", false, "Falta el perfil de AppArmor de Tatana: la ventana corre sin sandbox.");
    }

    /// <summary>true si <paramref name="version"/> (p. ej. <c>2.39</c>) es ≥ <paramref name="min"/>. Null o basura → false.</summary>
    public static bool VersionAtLeast(string? version, string min)
    {
        if (!TryParseVersion(version, out var v) || !TryParseVersion(min, out var m)) return false;
        return v >= m;
    }

    private static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Trim().Split('.');
        if (parts.Length == 1) parts = [parts[0], "0"];
        return Version.TryParse(string.Join('.', parts.Take(4)), out version!);
    }
}

/// <summary>Llamadas a la libc de Linux (solo se invocan en Linux).</summary>
internal static class LinuxNative
{
    private const int R_OK = 4;
    private const int W_OK = 2;

    [DllImport("libc", EntryPoint = "gnu_get_libc_version")]
    private static extern IntPtr GnuGetLibcVersion();

    [DllImport("libc", EntryPoint = "access", SetLastError = true)]
    private static extern int Access([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int mode);

    public static string? GlibcVersion()
    {
        if (!OperatingSystem.IsLinux()) return null;
        try { return Marshal.PtrToStringAnsi(GnuGetLibcVersion()); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return null; }
    }

    public static bool CanReadWrite(string path)
    {
        if (OperatingSystem.IsWindows()) return false;
        try { return Access(path, R_OK | W_OK) == 0; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }
}
