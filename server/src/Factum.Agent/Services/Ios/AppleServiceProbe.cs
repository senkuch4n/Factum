using System.Net;
using System.Net.Sockets;

namespace Factum.Agent.Services.Ios;

public enum AppleServiceState { Ok, Missing, Unknown }

/// <summary>
/// Sonda del servicio de dispositivos de Apple (SDD ios-herramientas-windows §6.5, D4).
/// Windows: Apple Mobile Device Service escucha en 127.0.0.1:27015 (lo mismo que usa
/// pymobiledevice3) → conexión TCP con timeout de 500 ms. macOS/Linux: existe /var/run/usbmuxd.
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
    private readonly bool _isWindows;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private (AppleServiceState State, DateTimeOffset At)? _cached;

    public AppleServiceProbe()
        : this(DefaultWindowsConnectAsync, () => File.Exists(UnixSocketPath), OperatingSystem.IsWindows())
    {
    }

    /// <param name="windowsConnect">true = conectó; false = rechazo o timeout; excepción = estado desconocido.</param>
    internal AppleServiceProbe(Func<CancellationToken, Task<bool>> windowsConnect, Func<bool> unixSocketExists,
        bool isWindows, Func<DateTimeOffset>? clock = null)
    {
        _windowsConnect = windowsConnect;
        _unixSocketExists = unixSocketExists;
        _isWindows = isWindows;
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
        if (!_isWindows)
        {
            try { return _unixSocketExists() ? AppleServiceState.Ok : AppleServiceState.Missing; }
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
