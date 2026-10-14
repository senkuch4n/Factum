using System.Globalization;

namespace Factum.Backend.Services.Auth;

/// <summary>
/// sesion-larga §5.1 / D11: validación al arrancar de la vida del token y del tope de sesión. Pura, como
/// <c>SecuritySettings.Resolve</c>: lee las claves crudas con <see cref="CultureInfo.InvariantCulture"/>
/// (punto decimal, sin importar la cultura del hilo) y devuelve errores (no arranca) y warnings.
/// </summary>
public static class JwtSettings
{
    public const int MaxHours = 168;
    public const string ExpiryHoursKey = "Jwt:ExpiryHours";
    public const string SessionMaxHoursKey = "Jwt:SessionMaxHours";
    public const string ExpiryHoursEnv = "FACTUM_JWT_EXPIRY_HOURS";
    public const string SessionMaxHoursEnv = "FACTUM_SESION_TOPE_HORAS";
    public const double DefaultExpiryHours = 2;
    public const double DefaultSessionMaxHours = 12;

    public static (List<string> Errors, List<string> Warnings) Validate(IConfiguration config)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var ttl = Read(config, ExpiryHoursKey, ExpiryHoursEnv, DefaultExpiryHours, errors);
        var max = Read(config, SessionMaxHoursKey, SessionMaxHoursEnv, DefaultSessionMaxHours, errors);
        if (ttl is { } t && max is { } m && m < t)
            warnings.Add(
                $"Sesión: el tope {SessionMaxHoursKey} ({SessionMaxHoursEnv}) = {Format(m)} h es menor que la duración " +
                $"del token {ExpiryHoursKey} ({ExpiryHoursEnv}) = {Format(t)} h: cada token se recorta al tope.");
        return (errors, warnings);
    }

    /// <summary>Valor válido (o el default si la clave falta), o null si es inválido (con el error agregado).</summary>
    private static double? Read(IConfiguration config, string key, string env, double fallback, List<string> errors)
    {
        var raw = config[key];
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        if (double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
            double.IsFinite(value) && value > 0 && value <= MaxHours)
            return value;
        errors.Add($"{key} ({env}) tiene que ser un número de horas mayor que 0 y hasta {MaxHours}, " +
                   $"con punto decimal (p. ej. 2 o 0.5); vino \"{raw.Trim()}\"");
        return null;
    }

    public static string Format(double hours) => hours.ToString("0.###", CultureInfo.InvariantCulture);
}
