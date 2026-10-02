using Factum.Backend.Services.Auth;

namespace Factum.Backend.Services.Support;

/// <summary>
/// Estado resuelto de la integración de soporte. Si <see cref="Errors"/> no está vacío, el
/// backend no arranca (ver <c>Program.cs</c>).
/// </summary>
public sealed record SupportSettings(
    bool Enabled,
    SupportIntegrationOptions Options,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public const string DisabledMessage = "La integración de soporte no está habilitada";
}

/// <summary>
/// Lee y valida <c>Integrations:Support</c>. Función pura: no loguea ni tira. Ningún warning ni
/// error incluye el valor de <c>ServiceKey</c> (solo el nombre de la clave).
/// </summary>
public static class SupportSettingsResolver
{
    private const string Section = "Integrations:Support";

    // LEGADO (compatibilidad con instalaciones viejas): borrar en una versión futura.
    private const string LegacySection = "FaroIntegration";

    private static readonly string[] Keys = ["BaseUrl", "ServiceKey", "TimeoutSeconds", "FrontendUrl"];

    public static SupportSettings Resolve(IConfiguration config)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var opts = new SupportIntegrationOptions();

        var rawEnabled = config[$"{Section}:Enabled"];
        var enabled = false;
        if (!string.IsNullOrWhiteSpace(rawEnabled) && !bool.TryParse(rawEnabled.Trim(), out enabled))
        {
            errors.Add($"{Section}:Enabled=\"{rawEnabled}\" no es válido: tiene que ser true o false.");
            return new SupportSettings(false, opts, warnings, errors);
        }

        if (!enabled)
        {
            if (Keys.Any(k => !string.IsNullOrWhiteSpace(config[$"{LegacySection}:{k}"])))
                warnings.Add($"La sección {LegacySection} es legado y no activa el soporte. Para activarlo: " +
                             $"{Section}:Enabled=true + BaseUrl, ServiceKey y FrontendUrl (ver README).");
            return new SupportSettings(false, opts, warnings, errors);
        }

        var values = new Dictionary<string, string?>();
        var fromLegacy = new List<string>();
        foreach (var k in Keys)
        {
            var current = config[$"{Section}:{k}"];
            var legacy = config[$"{LegacySection}:{k}"];
            if (string.IsNullOrWhiteSpace(current) && !string.IsNullOrWhiteSpace(legacy))
            {
                values[k] = legacy;
                fromLegacy.Add($"{LegacySection}:{k} → {Section}:{k}");
            }
            else
            {
                values[k] = current;
            }
        }
        if (fromLegacy.Count > 0)
            warnings.Add("Soporte: se tomaron claves de la sección legada; renombrarlas: " + string.Join(", ", fromLegacy) + ".");

        opts.Enabled = true;
        opts.BaseUrl = values["BaseUrl"]?.Trim() ?? string.Empty;
        opts.ServiceKey = values["ServiceKey"]?.Trim() ?? string.Empty;
        opts.FrontendUrl = values["FrontendUrl"]?.Trim() ?? string.Empty;
        opts.TimeoutSeconds = AuthSettingsResolver.ParseTimeout(values["TimeoutSeconds"], $"{Section}:TimeoutSeconds", errors);

        if (!AuthSettingsResolver.IsHttpUrl(opts.BaseUrl))
            errors.Add($"{Section}:BaseUrl=\"{opts.BaseUrl}\" no es válido: con {Section}:Enabled=true tiene que ser una URL absoluta http/https.");
        if (string.IsNullOrEmpty(opts.ServiceKey))
            errors.Add($"{Section}:ServiceKey está vacía: con {Section}:Enabled=true es obligatoria.");
        if (!AuthSettingsResolver.IsHttpUrl(opts.FrontendUrl))
            errors.Add($"{Section}:FrontendUrl=\"{opts.FrontendUrl}\" no es válido: con {Section}:Enabled=true tiene que ser una URL absoluta http/https.");

        return new SupportSettings(errors.Count == 0, opts, warnings, errors);
    }
}
