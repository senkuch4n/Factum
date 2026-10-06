namespace Factum.Backend.Infrastructure;

/// <summary>
/// Lectura y validación de <c>Cors:AllowedOrigins</c> (zip-local-informe-servidor §4.1 y §5.9).
/// Pura: no registra nada; <c>Program.cs</c> suma los errores a <c>configErrors</c>.
/// </summary>
public static class CorsOrigins
{
    public const string SectionKey = "Cors:AllowedOrigins";

    /// <summary>Default si la clave falta o está vacía: el front de desarrollo y de la instalación local.</summary>
    public static readonly IReadOnlyList<string> Defaults = ["http://localhost:3000", "http://127.0.0.1:3000"];

    public static (IReadOnlyList<string> Origins, List<string> Errors) Parse(IConfiguration config) =>
        Parse(config.GetSection(SectionKey).Get<string[]>(), SectionKey);

    /// <summary>
    /// Cada valor tiene que ser un origen absoluto <c>http</c>/<c>https</c> sin path, query,
    /// fragmento ni usuario; se le saca la <c>/</c> final. <c>"*"</c> o un valor inválido es un
    /// error. Lista vacía o null → <see cref="Defaults"/>.
    /// </summary>
    public static (IReadOnlyList<string> Origins, List<string> Errors) Parse(IEnumerable<string?>? values,
        string key = SectionKey)
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
                    ? $"{key} no admite \"*\": listá los orígenes permitidos (p. ej. https://factum.ejemplo.com)"
                    : $"{key}: \"{raw}\" no es un origen válido (http(s)://host[:puerto], sin path, query ni fragmento)");
            }
        }
        return (origins, errors);
    }

    /// <summary>Normaliza a <c>scheme://host[:puerto]</c> (host en minúscula, sin puerto por defecto).</summary>
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
        // "http://host/?" o "http://host/#" dejan Query/Fragment vacíos: se rechazan por el texto.
        if (value.Contains('?') || value.Contains('#')) return false;
        origin = $"{uri.Scheme}://{uri.Authority}";
        return true;
    }
}
