using System.Globalization;

namespace Factum.Backend.Services.Timestamping;

/// <summary>
/// Sección <c>Timestamping</c> de la config (sellado-tiempo §5.6). Se lee como texto y la valida
/// <see cref="Resolve"/>: un valor fuera de rango no arranca el backend, con el nombre de la clave y de la
/// variable de despliegue en el mensaje.
/// </summary>
public sealed class TimestampingOptions
{
    public const string Section = "Timestamping";

    public const int DefaultTimeoutSeconds = 10;
    public const int DefaultMaxAttempts = 12;
    public const int DefaultInlineBudgetSeconds = 10;
    public const int DefaultGenerationBudgetSeconds = 15;
    public const int DefaultInlineCooldownSeconds = 120;
    public const int DefaultPollSeconds = 30;

    /// <summary>
    /// Config resuelta y validada. <see cref="Urls"/> conserva el userinfo (si lo hay) porque hace falta para la
    /// autenticación Basic: nunca se loguea ni se persiste sin pasar por <see cref="SanitizeUrl"/>.
    /// </summary>
    public sealed record Settings(
        bool Enabled,
        IReadOnlyList<Uri> Urls,
        int TimeoutSeconds,
        int MaxAttempts,
        int InlineBudgetSeconds,
        int GenerationBudgetSeconds,
        int InlineCooldownSeconds,
        int PollSeconds)
    {
        /// <summary>Activo = <c>Enabled</c> y al menos una URL.</summary>
        public bool IsActive => Enabled && Urls.Count > 0;

        public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);
        public TimeSpan InlineBudget => TimeSpan.FromSeconds(InlineBudgetSeconds);
        public TimeSpan GenerationBudget => TimeSpan.FromSeconds(GenerationBudgetSeconds);
        public TimeSpan InlineCooldown => TimeSpan.FromSeconds(InlineCooldownSeconds);
        public TimeSpan Poll => TimeSpan.FromSeconds(PollSeconds);
        /// <summary>Presupuesto de un reintento de la cola: el timeout por cada URL.</summary>
        public TimeSpan QueueBudget => TimeSpan.FromSeconds((long)TimeoutSeconds * Math.Max(1, Urls.Count));

        /// <summary>Hosts (con puerto si no es el default), sin userinfo: lo único que va al log.</summary>
        public IReadOnlyList<string> Hosts => Urls.Select(u => u.IsDefaultPort ? u.Host : $"{u.Host}:{u.Port}").ToList();

        public static Settings Disabled { get; } = new(false, [], DefaultTimeoutSeconds, DefaultMaxAttempts,
            DefaultInlineBudgetSeconds, DefaultGenerationBudgetSeconds, DefaultInlineCooldownSeconds,
            DefaultPollSeconds);
    }

    /// <summary>Lee y valida la sección. Con errores, el backend no arranca (Program.cs suma la lista).</summary>
    public static (Settings Settings, List<string> Errors) Resolve(IConfiguration config)
    {
        var section = config.GetSection(Section);
        var errors = new List<string>();

        var urls = ParseUrls(section["Urls"], errors);

        var enabled = true;
        var rawEnabled = section["Enabled"];
        if (!string.IsNullOrWhiteSpace(rawEnabled) && !bool.TryParse(rawEnabled.Trim(), out enabled))
        {
            errors.Add("Timestamping:Enabled (FACTUM_SELLADO_HABILITADO) tiene que ser true o false");
            enabled = true;
        }

        var timeout = ReadInt(section, "TimeoutSeconds", "FACTUM_SELLADO_TIMEOUT_SEGUNDOS", DefaultTimeoutSeconds, 2, 60, errors);
        var maxAttempts = ReadInt(section, "MaxAttempts", "FACTUM_SELLADO_MAX_INTENTOS", DefaultMaxAttempts, 1, 50, errors);
        var inline = ReadInt(section, "InlineBudgetSeconds", null, DefaultInlineBudgetSeconds, 1, 60, errors);
        var generation = ReadInt(section, "GenerationBudgetSeconds", null, DefaultGenerationBudgetSeconds, 1, 120, errors);
        var cooldown = ReadInt(section, "InlineCooldownSeconds", null, DefaultInlineCooldownSeconds, 0, 3600, errors);
        var poll = ReadInt(section, "PollSeconds", null, DefaultPollSeconds, 5, 3600, errors);

        return (new Settings(enabled, urls, timeout, maxAttempts, inline, generation, cooldown, poll), errors);
    }

    // Separadas por coma, espacios o saltos de línea; las vacías se ignoran. El mensaje de error nunca
    // repite la URL entera (podría traer usuario y clave): solo su posición y, si se pudo leer, el host.
    internal static List<Uri> ParseUrls(string? raw, List<string> errors)
    {
        var list = new List<Uri>();
        if (string.IsNullOrWhiteSpace(raw)) return list;
        var parts = raw.Split([',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i].Trim();
            var label = $"Timestamping:Urls (FACTUM_SELLADO_URLS): la URL n.º {i + 1}";
            if (!Uri.TryCreate(part, UriKind.Absolute, out var uri))
            {
                errors.Add($"{label} no es una URL absoluta");
                continue;
            }
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                errors.Add($"{label} tiene que ser http o https");
                continue;
            }
            if (part.Contains('#') || uri.Fragment.Length > 0)
            {
                errors.Add($"{label} ({uri.Host}) no puede llevar fragmento (#)");
                continue;
            }
            list.Add(uri);
        }
        return list;
    }

    private static int ReadInt(IConfigurationSection section, string key, string? envName, int def, int min, int max,
        List<string> errors)
    {
        var raw = section[key];
        var name = envName is null ? $"Timestamping:{key}" : $"Timestamping:{key} ({envName})";
        if (string.IsNullOrWhiteSpace(raw)) return def;
        if (!int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
        {
            errors.Add($"{name} tiene que ser un número entero");
            return def;
        }
        if (v < min || v > max)
        {
            errors.Add($"{name} tiene que estar entre {min} y {max}");
            return def;
        }
        return v;
    }

    /// <summary><c>scheme://host[:port]/path</c>: sin userinfo, query ni fragmento (D-T6).</summary>
    public static string SanitizeUrl(Uri url) =>
        url.GetComponents(UriComponents.Scheme | UriComponents.Host | UriComponents.Port | UriComponents.Path,
            UriFormat.UriEscaped);

    /// <summary>Host (con puerto si no es el default), para el log y los mensajes de error.</summary>
    public static string HostOf(Uri url) => url.IsDefaultPort ? url.Host : $"{url.Host}:{url.Port}";
}
