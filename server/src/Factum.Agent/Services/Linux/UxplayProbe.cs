using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Factum.Agent.Common;

namespace Factum.Agent.Services.Linux;

/// <summary>Lo que dice <c>uxplay -h</c>: si soporta <c>-mp4</c> (UxPlay ≥ 1.73, E10) y la versión si la imprime.</summary>
public sealed record UxplayInfo(bool SupportsMp4, string? Version);

/// <summary>
/// Sondeo de <c>uxplay -h</c> (SDD tatana-linux D-T25, §5.2): una sola corrida en segundo plano por ruta (y fecha del
/// archivo, para notar una actualización), con timeout de 3 s y caché. Lo usan <c>IosService.AirplayAvailable</c> y
/// el chequeo <c>uxplay</c> de <see cref="LinuxHostProbe"/>.
/// </summary>
public sealed partial class UxplayProbe
{
    public static readonly UxplayProbe Shared = new();

    internal static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(3);
    // Espera del primer pedido: un poco más que el timeout del proceso, así la primera respuesta ya es real.
    private static readonly TimeSpan FirstWait = RunTimeout + TimeSpan.FromMilliseconds(500);

    private readonly Func<string, Task<string>> _runHelp;
    private readonly ConcurrentDictionary<string, Lazy<Task<UxplayInfo>>> _cache = new(StringComparer.Ordinal);

    public UxplayProbe() : this(DefaultRunHelpAsync) { }

    internal UxplayProbe(Func<string, Task<string>> runHelp) => _runHelp = runHelp;

    /// <summary>
    /// Info de <paramref name="uxplayPath"/>, o null si el sondeo no terminó todavía (no bloquea más de ~3,5 s y solo
    /// la primera vez por ruta).
    /// </summary>
    public UxplayInfo? Get(string uxplayPath)
    {
        var key = CacheKey(uxplayPath);
        var lazy = _cache.GetOrAdd(key, _ => new Lazy<Task<UxplayInfo>>(() => Task.Run(() => ProbeAsync(uxplayPath))));
        var task = lazy.Value;
        if (!task.IsCompleted)
        {
            try { task.Wait(FirstWait); } catch { /* ProbeAsync no lanza */ }
        }
        return task.IsCompletedSuccessfully ? task.Result : null;
    }

    private async Task<UxplayInfo> ProbeAsync(string path)
    {
        try { return Parse(await _runHelp(path)); }
        catch { return new UxplayInfo(false, null); }
    }

    private static string CacheKey(string path)
    {
        try { return path + "|" + File.GetLastWriteTimeUtc(path).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture); }
        catch { return path; }
    }

    [GeneratedRegex(@"UxPlay\s+v?(\d+\.\d+(?:\.\d+)?)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex VersionRegex();

    [GeneratedRegex(@"(^|\s)-mp4(\s|$|\[)", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex Mp4Regex();

    /// <summary>Puro: <c>-mp4</c> como opción en el texto de ayuda y la versión (<c>UxPlay 1.68</c> → <c>1.68</c>).</summary>
    public static UxplayInfo Parse(string helpText)
    {
        var text = helpText ?? string.Empty;
        var m = VersionRegex().Match(text);
        return new UxplayInfo(Mp4Regex().IsMatch(text), m.Success ? m.Groups[1].Value : null);
    }

    // `uxplay -h` imprime la ayuda (en stdout o stderr según la versión) y sale; se juntan las dos salidas.
    private static async Task<string> DefaultRunHelpAsync(string path)
    {
        var r = await ProcessRunner.RunArgumentListAsync(path, ["-h"], timeout: RunTimeout);
        return r.Stdout + "\n" + r.Stderr;
    }
}

/// <summary>Regla de AirPlay en Linux (D-T25), pura y testeada.</summary>
public static class AirplayRules
{
    public const string AvahiSocketPath = "/run/avahi-daemon/socket";

    public const string UxplayNotFound = "uxplay_not_found";
    public const string UxplayWithoutMp4 = "uxplay_without_mp4";
    public const string AvahiNotRunning = "avahi_not_running";

    /// <summary>Motivo de <c>ios.airplay_unavailable_reason</c> en Linux, o null si AirPlay está disponible.</summary>
    public static string? LinuxReason(bool uxplayFound, bool supportsMp4, bool avahiRunning) =>
        !uxplayFound ? UxplayNotFound
        : !supportsMp4 ? UxplayWithoutMp4
        : !avahiRunning ? AvahiNotRunning
        : null;

    /// <summary>
    /// Argumentos extra de uxplay en Linux: <c>-p</c> (puertos fijos TCP 7100/7000/7001 y UDP 6000/6001/7011, los que
    /// abre "Preparar esta PC" en el firewall). Va al final para que uxplay no tome el argumento siguiente como puerto.
    /// </summary>
    public static IReadOnlyList<string> ExtraArgs(HostOs os) => os == HostOs.Linux ? ["-p"] : [];
}
