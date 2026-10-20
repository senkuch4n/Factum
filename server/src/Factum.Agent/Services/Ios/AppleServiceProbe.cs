using System.Net;
using System.Net.Sockets;
using Factum.Agent.Common;

namespace Factum.Agent.Services.Ios;

public enum AppleServiceState { Ok, Missing, Unknown }

/// <summary>
/// Sonda del servicio de dispositivos de Apple (SDD ios-herramientas-windows §6.5, D4).
/// Windows: Apple Mobile Device Service escucha en 127.0.0.1:27015 (lo mismo que usa
/// pymobiledevice3) → conexión TCP con timeout de 500 ms. macOS: existe /var/run/usbmuxd. Linux
/// (tatana-linux D-T12): existe el socket <b>o</b> usbmuxd está instalado (lo levanta udev al enchufar el
/// iPhone, así que sin iPhone el socket no existe aunque esté todo bien).
/// Resultado cacheado 5 s (lo usan /health y el polling de dispositivos). Singleton en DI.
/// </summary>
public sealed class AppleServiceProbe
{
    public const int WindowsPort = 27015;
    public const string UnixSocketPath = "/var/run/usbmuxd";
    internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(500);
    internal static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(5);

    private readonly Func<CancellationToken, Task<bool>> _windowsConnect;
    private readonly Func<bool> _unixSocketExists;
    private readonly Func<bool> _usbmuxdInstalled;
    private readonly HostOs _os;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private (AppleServiceState State, DateTimeOffset At)? _cached;

    public AppleServiceProbe()
        : this(DefaultWindowsConnectAsync, () => File.Exists(UnixSocketPath), HostOsInfo.Current,
            usbmuxdInstalled: () => IsUsbmuxdInstalled(File.Exists))
    {
    }

    /// <summary>Binarios y unidades de systemd que indican que el paquete usbmuxd está instalado (Linux).</summary>
    public static readonly IReadOnlyList<string> UsbmuxdInstalledPaths =
    [
        "/usr/sbin/usbmuxd", "/usr/bin/usbmuxd",
        "/usr/lib/systemd/system/usbmuxd.service", "/lib/systemd/system/usbmuxd.service",
    ];

    /// <summary>true si existe alguna de <see cref="UsbmuxdInstalledPaths"/> (testeable con un <paramref name="exists"/> falso).</summary>
    public static bool IsUsbmuxdInstalled(Func<string, bool> exists) => UsbmuxdInstalledPaths.Any(exists);

    /// <param name="windowsConnect">true = conectó; false = rechazo o timeout; excepción = estado desconocido.</param>
    /// <param name="usbmuxdInstalled">Solo Linux: true si el paquete usbmuxd está instalado (sin él, false).</param>
    internal AppleServiceProbe(Func<CancellationToken, Task<bool>> windowsConnect, Func<bool> unixSocketExists,
        HostOs os, Func<DateTimeOffset>? clock = null, Func<bool>? usbmuxdInstalled = null)
    {
        _windowsConnect = windowsConnect;
        _unixSocketExists = unixSocketExists;
        _usbmuxdInstalled = usbmuxdInstalled ?? (() => false);
        _os = os;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<AppleServiceState> GetStateAsync(CancellationToken ct = default)
    {
        var now = _clock();
        lock (_gate)
        {
            if (_cached is { } c && now - c.At < CacheDuration) return c.State;
        }

        var state = await ProbeAsync(ct);
        lock (_gate) _cached = (state, _clock());
        return state;
    }

    public static string ToJson(AppleServiceState state) => state switch
    {
        AppleServiceState.Ok      => "ok",
        AppleServiceState.Missing => "missing",
        _                         => "unknown",
    };

    private async Task<AppleServiceState> ProbeAsync(CancellationToken ct)
    {
        if (_os == HostOs.MacOS)
        {
            try { return _unixSocketExists() ? AppleServiceState.Ok : AppleServiceState.Missing; }
            catch { return AppleServiceState.Unknown; }
        }
        if (_os == HostOs.Linux)
        {
            try
            {
                return _unixSocketExists() || _usbmuxdInstalled() ? AppleServiceState.Ok : AppleServiceState.Missing;
            }
            catch { return AppleServiceState.Unknown; }
        }

        try
        {
            return await _windowsConnect(ct) ? AppleServiceState.Ok : AppleServiceState.Missing;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return AppleServiceState.Unknown;
        }
    }

    private static async Task<bool> DefaultWindowsConnectAsync(CancellationToken ct)
    {
        using var client = new TcpClient(AddressFamily.InterNetwork);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ConnectTimeout);
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, WindowsPort, timeout.Token);
            return true;
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.ConnectionRefused
                                             or SocketError.TimedOut or SocketError.HostUnreachable
                                             or SocketError.NetworkUnreachable)
        {
            return false;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false; // timeout de 500 ms
        }
    }
}
