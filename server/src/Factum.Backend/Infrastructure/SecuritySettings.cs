using System.Globalization;
using System.Net;

namespace Factum.Backend.Infrastructure;

/// <summary>api-hardening §4.6: límites del rate limit del login (por IP y por DNI).</summary>
public sealed class LoginRateLimitOptions
{
    public int PerIpPermitLimit { get; set; } = 10;
    public int PerIpWindowSeconds { get; set; } = 60;
    public int PerDniPermitLimit { get; set; } = 5;
    public int PerDniWindowSeconds { get; set; } = 60;
}

/// <summary>
/// api-hardening §4.5/§4.6: configuración de la sección <c>Security</c>. Los defaults viven en código
/// (no en appsettings.json): las listas de configuración se mezclan por índice y un override con menos
/// elementos dejaría restos del default.
/// </summary>
public sealed record SecuritySettings(LoginRateLimitOptions LoginRateLimit, IReadOnlyList<IPNetwork> KnownNetworks)
{
    public const string KnownNetworksKey = "Security:ForwardedHeaders:KnownNetworks";
    private const string RateLimitSection = "Security:LoginRateLimit";

    /// <summary>
    /// Proxies confiables por defecto (D4 A): loopback, RFC 1918 y ULA. Las IPv4 mapeadas en IPv6
    /// (<c>::ffff:a.b.c.d</c>) no necesitan entrada propia: <c>ForwardedHeadersMiddleware</c> las
    /// convierte a IPv4 antes de comparar (verificado en FactumEdgeTests).
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultKnownNetworks =
        ["127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "fc00::/7"];

    public static SecuritySettings Defaults { get; } =
        new(new LoginRateLimitOptions(), [.. DefaultKnownNetworks.Select(n => IPNetwork.Parse(n))]);

    /// <summary>Pura, como <see cref="CorsOrigins.Parse(IConfiguration)"/>: settings + errores para el fail-fast.</summary>
    public static (SecuritySettings Settings, List<string> Errors) Resolve(IConfiguration config)
    {
        var errors = new List<string>();
        var rl = new LoginRateLimitOptions
        {
            PerIpPermitLimit = ReadPositive(config, nameof(LoginRateLimitOptions.PerIpPermitLimit), 10, errors),
            PerIpWindowSeconds = ReadPositive(config, nameof(LoginRateLimitOptions.PerIpWindowSeconds), 60, errors),
            PerDniPermitLimit = ReadPositive(config, nameof(LoginRateLimitOptions.PerDniPermitLimit), 5, errors),
            PerDniWindowSeconds = ReadPositive(config, nameof(LoginRateLimitOptions.PerDniWindowSeconds), 60, errors),
        };

        var raw = config.GetSection(KnownNetworksKey).Get<string?[]>() ?? [];
        var values = raw.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()).ToList();
        if (values.Count == 0) values = [.. DefaultKnownNetworks];

        var networks = new List<IPNetwork>();
        foreach (var value in values)
        {
            if (IPNetwork.TryParse(value, out var network))
            {
                if (!networks.Contains(network)) networks.Add(network);
            }
            else
            {
                errors.Add($"{KnownNetworksKey}: \"{value}\" no es una red válida (CIDR, p. ej. 10.0.0.0/8)");
            }
        }
        return (new SecuritySettings(rl, networks), errors);
    }

    private static int ReadPositive(IConfiguration config, string name, int fallback, List<string> errors)
    {
        var key = $"{RateLimitSection}:{name}";
        var raw = config[key];
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        if (int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0)
            return value;
        errors.Add($"{key} tiene que ser mayor que 0");
        return fallback;
    }
}
