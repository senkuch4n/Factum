namespace Factum.Agent.Common;

/// <summary>
/// Orígenes permitidos de Tatana (zip-local-informe-servidor §4.2 y §6.1). Pura: parseo y
/// validación con las mismas reglas que <c>Cors:AllowedOrigins</c> del backend
/// (Factum.Backend/Infrastructure/CorsOrigins.cs) y la comparación de la guarda.
/// </summary>
public static class OriginPolicy
{
    public const string Key = "Agent:AllowedOrigins";

    public static readonly IReadOnlyList<string> Defaults = ["http://localhost:3000", "http://127.0.0.1:3000"];

    /// <summary>
    /// Cada valor: origen absoluto <c>http</c>/<c>https</c> sin path, query, fragmento ni usuario
    /// (la <c>/</c> final se ignora). <c>"*"</c> o inválido → error. Vacía o null → <see cref="Defaults"/>.
    /// </summary>
    public static (IReadOnlyList<string> Origins, List<string> Errors) Parse(IEnumerable<string?>? values)
    {
        var errors = new List<string>();
        var list = values?.ToList() ?? [];
        if (list.Count == 0) return (Defaults, errors);

        var origins = new List<string>();
        foreach (var raw in list)
        {
            if (TryNormalize(raw, out var origin))
            {
                if (!origins.Contains(origin, StringComparer.OrdinalIgnoreCase)) origins.Add(origin);
            }
            else
            {
                errors.Add(raw?.Trim() == "*"
                    ? $"{Key} no admite \"*\": listá los orígenes permitidos"
                    : $"{Key}: \"{raw}\" no es un origen válido (http(s)://host[:puerto], sin path, query ni fragmento)");
            }
        }
        return (origins, errors);
    }

    public static bool TryNormalize(string? raw, out string origin)
    {
        origin = "";
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value) || value == "*") return false;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo) || string.IsNullOrEmpty(uri.Host)) return false;
        if (uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            return false;
        if (value.Contains('?') || value.Contains('#')) return false;
        origin = $"{uri.Scheme}://{uri.Authority}";
        return true;
    }

    /// <summary>
    /// ¿El header <c>Origin</c> está en la lista? Ordinal sin distinguir mayúsculas y sin la
    /// <c>/</c> final. null, vacío o <c>"null"</c> (sandbox, file://) → no permitido.
    /// </summary>
    public static bool IsAllowed(string? origin, IReadOnlyList<string> allowed)
    {
        if (string.IsNullOrWhiteSpace(origin)) return false;
        var value = origin.Trim().TrimEnd('/');
        if (value.Equals("null", StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var a in allowed)
            if (string.Equals(a, value, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
