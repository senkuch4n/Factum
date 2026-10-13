using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Factum.Agent.Common;

/// <summary>
/// Hosts que Tatana atiende (tatana-ws-y-hosts §4.3, D3 = B). Pura y testeable: arma la lista para
/// <c>HostFilteringOptions.AllowedHosts</c> y así cierra el DNS rebinding (un sitio cuyo dominio resuelve a
/// 127.0.0.1 llega con su propio <c>Host</c> y recibe 400).
/// </summary>
public static class HostPolicy
{
    public const string Key = "Agent:AllowedHosts";

    /// <summary>Siempre permitidos. Las IPv6 van entre corchetes (así las compara HostFilteringMiddleware).</summary>
    public static readonly IReadOnlyList<string> LocalHosts = ["localhost", "127.0.0.1", "[::1]"];

    /// <summary>
    /// Lista para <c>HostFilteringOptions.AllowedHosts</c>: <see cref="LocalHosts"/> + IP de bind (si es concreta)
    /// + IP locales (si bind es <c>0.0.0.0</c>/<c>::</c>) + extras normalizados. Sin duplicados
    /// (OrdinalIgnoreCase). Un extra inválido va a <c>Errors</c> y no a la lista.
    /// </summary>
    public static (IReadOnlyList<string> Hosts, List<string> Errors) Build(
        string bindAddress, IEnumerable<string?>? extra, Func<IEnumerable<IPAddress>> localAddresses)
    {
        var errors = new List<string>();
        var hosts = new List<string>(LocalHosts);

        void Add(string h)
        {
            if (!hosts.Contains(h, StringComparer.OrdinalIgnoreCase)) hosts.Add(h);
        }

        if (IPAddress.TryParse(bindAddress?.Trim(), out var bind))
        {
            if (bind.Equals(IPAddress.Any) || bind.Equals(IPAddress.IPv6Any))
            {
                var includeV6 = bind.Equals(IPAddress.IPv6Any);
                foreach (var ip in localAddresses())
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork ||
                        (includeV6 && ip.AddressFamily == AddressFamily.InterNetworkV6))
                        Add(Format(ip));
                }
            }
            else if (!IPAddress.IsLoopback(bind))
            {
                Add(Format(bind));
            }
        }

        foreach (var raw in extra ?? [])
        {
            if (TryNormalize(raw, out var host)) Add(host);
            else errors.Add(ErrorFor(raw));
        }
        return (hosts, errors);
    }

    /// <summary>Mensaje de error de una entrada inválida de <c>Agent:AllowedHosts</c> (castellano, estilo OriginPolicy).</summary>
    public static string ErrorFor(string? raw) =>
        raw is not null && raw.Contains('*')
            ? $"{Key} no admite \"*\": listá los hosts permitidos"
            : $"{Key}: \"{raw}\" no es un host válido (nombre o IP, sin esquema, puerto ni path)";

    /// <summary>
    /// Valida y normaliza una entrada de <c>Agent:AllowedHosts</c>: nombre DNS, IPv4 o IPv6 (con o sin corchetes).
    /// Sin esquema, puerto, path, <c>*</c> ni espacios. IPv6 → <c>[addr]</c> sin scope id. Todo en minúsculas.
    /// </summary>
    public static bool TryNormalize(string? raw, out string host)
    {
        host = "";
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value)) return false;
        if (value.Contains('*') || value.Contains("://")) return false;
        if (value.IndexOfAny(['/', '?', '#', '@', ' ', '\t', '\\']) >= 0) return false;

        // IPv6 entre corchetes: "[::1]" (con algo después del corchete = puerto → inválido).
        if (value.StartsWith('['))
        {
            if (!value.EndsWith(']')) return false;
            var inner = value[1..^1];
            if (!IPAddress.TryParse(inner, out var v6) || v6.AddressFamily != AddressFamily.InterNetworkV6)
                return false;
            host = Format(v6);
            return true;
        }

        var colons = value.Count(c => c == ':');
        if (colons == 1) return false; // host:puerto
        if (colons > 1)
        {
            if (!IPAddress.TryParse(value, out var v6) || v6.AddressFamily != AddressFamily.InterNetworkV6)
                return false;
            host = Format(v6);
            return true;
        }

        if (IPAddress.TryParse(value, out var v4) && v4.AddressFamily == AddressFamily.InterNetwork &&
            value.Count(c => c == '.') == 3)
        {
            host = v4.ToString();
            return true;
        }
        if (Uri.CheckHostName(value) != UriHostNameType.Dns) return false;
        // Una "IP" incompleta (p. ej. 10.1) pasa CheckHostName como Dns pero no es lo que el usuario quiso.
        if (value.All(c => char.IsDigit(c) || c == '.')) return false;
        host = value.ToLowerInvariant();
        return true;
    }

    /// <summary>IPv4 tal cual; IPv6 entre corchetes, sin scope id, en minúsculas.</summary>
    internal static string Format(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetworkV6) return ip.ToString();
        var copy = new IPAddress(ip.GetAddressBytes()); // sin ScopeId
        return "[" + copy.ToString().ToLowerInvariant() + "]";
    }

    /// <summary>IP unicast de las interfaces <c>Up</c> de esta PC (D-A3: se toman al arrancar).</summary>
    public static IEnumerable<IPAddress> MachineAddresses()
    {
        var list = new List<IPAddress>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                    list.Add(addr.Address);
            }
        }
        catch (NetworkInformationException) { /* sin interfaces: quedan los locales */ }
        return list;
    }
}
