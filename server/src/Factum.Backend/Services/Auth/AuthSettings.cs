using System.Globalization;

namespace Factum.Backend.Services.Auth;

/// <summary>
/// Configuración de autenticación resuelta al arrancar. <see cref="Mode"/> es siempre
/// <see cref="AuthModes.Dev"/>, <see cref="AuthModes.External"/> o <see cref="AuthModes.Local"/>.
/// <see cref="Local"/> no es null solo con <c>Mode=local</c>. Si <see cref="Errors"/> no está
/// vacío, el backend no arranca (ver <c>Program.cs</c>).
/// </summary>
public sealed record AuthSettings(
    string Mode,
    ExternalAuthOptions? External,
    Uri? ExternalLoginUri,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    LocalAuthOptions? Local = null,
    bool AllowDevOutsideDevelopment = false);

/// <summary>
/// Lee y valida la sección <c>Auth</c>. Función pura: no loguea ni tira; devuelve warnings y
/// errores para que <c>Program.cs</c> decida. Lee las claves crudas (<c>config["…"]</c>) para
/// distinguir una clave ausente de una vacía. Ningún mensaje incluye una contraseña.
/// </summary>
public static class AuthSettingsResolver
{
    private const int MinTimeout = 1;
    private const int MaxTimeout = 120;

    /// <summary>Valor de <c>Jwt:Secret</c> versionado en el repo: no sirve con <c>Mode=local</c> fuera de Development.</summary>
    public const string RepoJwtSecret = "factum-dev-secret-change-in-production";
    private const int MinLocalJwtSecretLength = 32;
    private const int MaxNameLength = 120;

    internal const string DevOutsideDevelopmentError =
        "Auth:Mode=dev acepta cualquier contraseña y no se permite fuera de Development. Usá Auth:Mode=local, o poné Auth:AllowDevOutsideDevelopment=true solo en una instalación local aislada.";

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
        else if (mode != AuthModes.Dev && mode != AuthModes.External && mode != AuthModes.Local)
        {
            errors.Add($"Auth:Mode=\"{rawMode}\" no es válido. Valores válidos: \"{AuthModes.Dev}\", \"{AuthModes.External}\", \"{AuthModes.Local}\".");
            return new AuthSettings(AuthModes.Dev, null, null, warnings, errors);
        }

        if (mode == AuthModes.Dev)
        {
            // usuarios-locales §4.3 (D1): dev fuera de Development exige el flag explícito.
            var allowDev = false;
            if (!isDevelopment)
            {
                var rawAllow = config["Auth:AllowDevOutsideDevelopment"];
                if (!string.IsNullOrWhiteSpace(rawAllow) && !bool.TryParse(rawAllow.Trim(), out allowDev))
                    errors.Add($"Auth:AllowDevOutsideDevelopment=\"{rawAllow}\" no es válido: tiene que ser true o false.");
                else if (!allowDev)
                    errors.Add(DevOutsideDevelopmentError);
                else
                    warnings.Add("Auth:Mode=dev acepta cualquier contraseña; no usar en producción.");
            }
            return new AuthSettings(AuthModes.Dev, null, null, warnings, errors, AllowDevOutsideDevelopment: allowDev);
        }

        if (mode == AuthModes.Local)
        {
            var local = ResolveLocal(config, isDevelopment, warnings, errors);
            return new AuthSettings(AuthModes.Local, null, null, warnings, errors, local);
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

    // ── Modo local (usuarios-locales §4.3) ───────────────────────────────────────
    private static LocalAuthOptions ResolveLocal(IConfiguration config, bool isDevelopment,
        List<string> warnings, List<string> errors)
    {
        // Jwt:Secret propio del despliegue.
        var secret = config["Jwt:Secret"] ?? string.Empty;
        if (secret == RepoJwtSecret || secret.Length < MinLocalJwtSecretLength)
        {
            const string msg = "Con Auth:Mode=local, Jwt:Secret tiene que ser propio de este despliegue (≥ 32 caracteres), no el valor del repo.";
            if (isDevelopment) warnings.Add(msg + " (Se tolera solo en Development.)");
            else errors.Add(msg);
        }

        var minLength = ParseIntInRange(config["Auth:Local:PasswordMinLength"], "Auth:Local:PasswordMinLength",
            8, 64, LocalAuthOptions.DefaultPasswordMinLength, errors);
        var maxFailed = ParseIntInRange(config["Auth:Local:MaxFailedAttempts"], "Auth:Local:MaxFailedAttempts",
            1, 50, 5, errors);
        var lockout = ParseIntInRange(config["Auth:Local:LockoutMinutes"], "Auth:Local:LockoutMinutes",
            1, 1440, 15, errors);

        // BootstrapSuperadmins: lista (JSON o Auth__Local__BootstrapSuperadmins__0__Dni=…).
        var bootstrap = new List<BootstrapSuperadmin>();
        var seenDnis = new HashSet<string>(StringComparer.Ordinal);
        foreach (var child in config.GetSection("Auth:Local:BootstrapSuperadmins").GetChildren())
        {
            var path = "Auth:Local:BootstrapSuperadmins:" + child.Key;
            var ok = true;

            var dni = child["Dni"]?.Trim() ?? string.Empty;
            if (!DniFormat.IsValid(dni))
            {
                errors.Add($"{path}:Dni=\"{dni}\" no es válido: tiene que tener 7 u 8 dígitos.");
                ok = false;
            }
            else if (!seenDnis.Add(dni))
            {
                errors.Add($"{path}:Dni={dni} está repetido en Auth:Local:BootstrapSuperadmins.");
                ok = false;
            }

            var name = child["Name"]?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                errors.Add($"{path}:Name no puede estar vacío.");
                ok = false;
            }
            else if (name.Length > MaxNameLength)
            {
                errors.Add($"{path}:Name tiene {name.Length} caracteres; el máximo es {MaxNameLength}.");
                ok = false;
            }

            var temp = child["TemporaryPassword"] ?? string.Empty;
            if (!ValidateTemporaryPassword(temp, $"{path}:TemporaryPassword", minLength, errors))
                ok = false;

            if (ok) bootstrap.Add(new BootstrapSuperadmin(dni, name, temp));
        }

        // ResetSuperadmin: vacío = apagado; Dni y TemporaryPassword van juntos.
        SuperadminReset? reset = null;
        var resetDni = config["Auth:Local:ResetSuperadmin:Dni"]?.Trim() ?? string.Empty;
        var resetTemp = config["Auth:Local:ResetSuperadmin:TemporaryPassword"] ?? string.Empty;
        if (resetDni.Length > 0 || resetTemp.Length > 0)
        {
            var ok = true;
            if (resetDni.Length == 0 || resetTemp.Length == 0)
            {
                errors.Add("Auth:Local:ResetSuperadmin:Dni y Auth:Local:ResetSuperadmin:TemporaryPassword van juntos: falta "
                    + (resetDni.Length == 0 ? "Dni." : "TemporaryPassword."));
                ok = false;
            }
            else
            {
                if (!DniFormat.IsValid(resetDni))
                {
                    errors.Add($"Auth:Local:ResetSuperadmin:Dni=\"{resetDni}\" no es válido: tiene que tener 7 u 8 dígitos.");
                    ok = false;
                }
                if (!ValidateTemporaryPassword(resetTemp, "Auth:Local:ResetSuperadmin:TemporaryPassword", minLength, errors))
                    ok = false;
            }
            if (ok) reset = new SuperadminReset(resetDni, resetTemp);
        }

        return new LocalAuthOptions
        {
            PasswordMinLength = minLength,
            MaxFailedAttempts = maxFailed,
            LockoutMinutes = lockout,
            BootstrapSuperadmins = bootstrap,
            ResetSuperadmin = reset,
        };
    }

    /// <summary>Largo entre el mínimo y 128. El mensaje nunca incluye el valor.</summary>
    private static bool ValidateTemporaryPassword(string value, string key, int minLength, List<string> errors)
    {
        if (value.Length < minLength)
        {
            errors.Add($"{key} tiene {value.Length} caracteres; el mínimo es {minLength}.");
            return false;
        }
        if (value.Length > LocalAuthOptions.PasswordMaxLength)
        {
            errors.Add($"{key} tiene {value.Length} caracteres; el máximo es {LocalAuthOptions.PasswordMaxLength}.");
            return false;
        }
        return true;
    }

    private static int ParseIntInRange(string? raw, string key, int min, int max, int fallback, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        if (int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v >= min && v <= max)
            return v;
        errors.Add($"{key}=\"{raw}\" no es válido: tiene que ser un entero entre {min} y {max}.");
        return fallback;
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
