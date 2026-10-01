using System.Globalization;

namespace Factum.Backend.Services.Auth;

/// <summary>
/// Configuración de autenticación resuelta al arrancar. <see cref="Mode"/> es siempre
/// <see cref="AuthModes.Dev"/> o <see cref="AuthModes.External"/>. Si <see cref="Errors"/> no está
/// vacío, el backend no arranca (ver <c>Program.cs</c>).
/// </summary>
public sealed record AuthSettings(
    string Mode,
    ExternalAuthOptions? External,
    Uri? ExternalLoginUri,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

/// <summary>
/// Lee y valida la sección <c>Auth</c>. Función pura: no loguea ni tira; devuelve warnings y
/// errores para que <c>Program.cs</c> decida. Lee las claves crudas (<c>config["…"]</c>) para
/// distinguir una clave ausente de una vacía.
/// </summary>
public static class AuthSettingsResolver
{
    private const int MinTimeout = 1;
    private const int MaxTimeout = 120;

    // LEGADO (compatibilidad con instalaciones viejas): borrar en una versión futura.
    private const string LegacyModeValue = "mpf";
    private static readonly (string Legacy, string Current)[] LegacyKeys =
    [
        ("Auth:MpfBaseUrl", "Auth:External:BaseUrl"),
        ("Auth:MpfLoginPath", "Auth:External:LoginPath"),
        ("Auth:MpfTimeoutSeconds", "Auth:External:TimeoutSeconds"),
    ];

    public static AuthSettings Resolve(IConfiguration config, bool isDevelopment)
    {
        var warnings = new List<string>();
        var errors = new List<string>();

        var rawMode = config["Auth:Mode"];
        var mode = string.IsNullOrWhiteSpace(rawMode) ? AuthModes.Dev : rawMode.Trim().ToLowerInvariant();

        if (mode == LegacyModeValue)
        {
            warnings.Add($"Auth:Mode={LegacyModeValue} es legado; usar Auth:Mode={AuthModes.External}.");
            mode = AuthModes.External;
        }
        else if (mode != AuthModes.Dev && mode != AuthModes.External)
        {
            errors.Add($"Auth:Mode=\"{rawMode}\" no es válido. Valores válidos: \"{AuthModes.Dev}\", \"{AuthModes.External}\".");
            return new AuthSettings(AuthModes.Dev, null, null, warnings, errors);
        }

        if (mode == AuthModes.Dev)
        {
            if (!isDevelopment)
                warnings.Add("Auth:Mode=dev acepta cualquier contraseña; no usar en producción.");
            return new AuthSettings(AuthModes.Dev, null, null, warnings, errors);
        }

        // ── Modo external: claves nuevas, con alias legado ──────────────────────
        var values = new Dictionary<string, string?>();
        var usedAliases = new List<string>();
        foreach (var (legacy, current) in LegacyKeys)
        {
            var currentValue = config[current];
            var legacyValue = config[legacy];
            var currentHas = !string.IsNullOrWhiteSpace(currentValue);
            var legacyHas = !string.IsNullOrWhiteSpace(legacyValue);

            if (currentHas)
            {
                values[current] = currentValue;
                if (legacyHas)
                    warnings.Add($"{legacy} se ignora porque está {current}.");
            }
            else if (legacyHas)
            {
                values[current] = legacyValue;
                usedAliases.Add($"{legacy} → {current}");
            }
            else
            {
                values[current] = currentValue;
            }
        }
        if (usedAliases.Count > 0)
            warnings.Add("Se usaron claves legadas de Auth; renombrarlas: " + string.Join(", ", usedAliases) + ".");

        var opts = new ExternalAuthOptions();

        // BaseUrl
        var baseUrl = values["Auth:External:BaseUrl"]?.Trim() ?? string.Empty;
        if (!IsHttpUrl(baseUrl))
            errors.Add($"Auth:External:BaseUrl=\"{baseUrl}\" no es válido: con Auth:Mode=external tiene que ser una URL absoluta http/https.");
        opts.BaseUrl = baseUrl;

        // LoginPath
        var loginPath = values["Auth:External:LoginPath"]?.Trim();
        if (string.IsNullOrEmpty(loginPath))
            loginPath = opts.LoginPath;
        else if (loginPath.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                 Uri.TryCreate(loginPath, UriKind.Absolute, out _))
            errors.Add($"Auth:External:LoginPath=\"{loginPath}\" no es válido: tiene que ser un path (por ejemplo \"/auth/login\"), no una URL.");
        opts.LoginPath = loginPath;

        // TimeoutSeconds
        opts.TimeoutSeconds = ParseTimeout(values["Auth:External:TimeoutSeconds"], "Auth:External:TimeoutSeconds", errors);

        // Request fields (nombres planos)
        opts.Request.DniField = OrDefault(config["Auth:External:Request:DniField"], opts.Request.DniField);
        opts.Request.UserField = OrDefault(config["Auth:External:Request:UserField"], opts.Request.UserField);
        opts.Request.PasswordField = OrDefault(config["Auth:External:Request:PasswordField"], opts.Request.PasswordField);
        var requestFields = new[]
        {
            ("Auth:External:Request:DniField", opts.Request.DniField),
            ("Auth:External:Request:UserField", opts.Request.UserField),
            ("Auth:External:Request:PasswordField", opts.Request.PasswordField),
        };
        foreach (var dup in requestFields.GroupBy(f => f.Item2, StringComparer.Ordinal).Where(g => g.Count() > 1))
            errors.Add($"{string.Join(" y ", dup.Select(d => d.Item1))} tienen el mismo valor \"{dup.Key}\": tienen que ser distintos.");

        // Response fields (admiten rutas con puntos)
        opts.Response.NameField = OrDefault(config["Auth:External:Response:NameField"], opts.Response.NameField);
        var rawSigla = config["Auth:External:Response:SiglaField"];
        opts.Response.SiglaField = rawSigla is null ? "sigla" : rawSigla.Trim();

        Uri? loginUri = null;
        if (errors.Count == 0)
        {
            var composed = baseUrl.TrimEnd('/') + "/" + loginPath.TrimStart('/');
            if (Uri.TryCreate(composed, UriKind.Absolute, out var u))
                loginUri = u;
            else
                errors.Add($"La URL de login \"{composed}\" (Auth:External:BaseUrl + Auth:External:LoginPath) no es válida.");
        }

        return new AuthSettings(AuthModes.External, opts, loginUri, warnings, errors);
    }

    internal static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    internal static int ParseTimeout(string? raw, string key, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 10;
        if (int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) &&
            t is >= MinTimeout and <= MaxTimeout)
            return t;
        errors.Add($"{key}=\"{raw}\" no es válido: tiene que ser un entero entre {MinTimeout} y {MaxTimeout}.");
        return 10;
    }

    private static string OrDefault(string? raw, string fallback) =>
        string.IsNullOrWhiteSpace(raw) ? fallback : raw.Trim();
}
