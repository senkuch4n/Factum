using System.Net;
using System.Reflection;
using Factum.Agent.Models;

namespace Factum.Agent.Common;

/// <summary>
/// Validación de VALORES del <c>appsettings.Local.json</c> (tatana-ws-y-hosts §4.4, D4 = A). Si devuelve un
/// texto, el archivo entero se ignora (como un JSON roto, D-T16) y Tatana arranca igual. Pura: no registra nada.
/// Solo mira la sección <c>Agent</c>; el <c>AllowedHosts</c> de la raíz (de ASP.NET) se ignora siempre.
/// </summary>
internal static class LocalConfigValidator
{
    private const int MaxValueChars = 60;

    /// <summary>null = válido. Si no, texto en castellano que nombra la(s) clave(s), unidas con "; ", sin punto final.</summary>
    public static string? Validate(IConfiguration localOnly)
    {
        var errors = new List<string>();

        // 1. Tipos (por reflexión): AgentOptions y sus secciones anidadas (VideoPostprocess, Recording).
        CheckTypes(localOnly, "Agent", typeof(AgentOptions), errors, depth: 0);

        // 2. Semántica, solo para las claves presentes en el archivo local (y si su tipo era válido).
        var agent = localOnly.GetSection("Agent");

        var origins = agent.GetSection("AllowedOrigins");
        if (origins.Exists() && !HasError(errors, "Agent:AllowedOrigins"))
        {
            var (_, originErrors) = OriginPolicy.Parse(ReadList(origins));
            errors.AddRange(originErrors);
        }

        if (TryGet<long>(agent, "MaxUploadBytes", errors, out var maxUpload) && maxUpload <= 0)
            errors.Add("Agent:MaxUploadBytes tiene que ser mayor que 0");
        if (TryGet<long>(agent, "MinFreeBytes", errors, out var minFree) && minFree < 0)
            errors.Add("Agent:MinFreeBytes tiene que ser mayor o igual que 0");
        if (TryGet<int>(agent, "Port", errors, out var port) && port is < 1 or > 65535)
            errors.Add("Agent:Port tiene que estar entre 1 y 65535");

        var bind = agent.GetSection("BindAddress");
        if (bind.Exists() && bind.Value is { } bindValue)
        {
            var b = bindValue.Trim();
            // Vacío = default (localhost), igual que Program.cs.
            if (b.Length > 0 && !string.Equals(b, "localhost", StringComparison.OrdinalIgnoreCase) &&
                !IPAddress.TryParse(b, out _))
                errors.Add($"Agent:BindAddress inválido: '{Cut(bindValue)}'. Usá localhost o una IP");
        }

        var hosts = agent.GetSection("AllowedHosts");
        if (hosts.Exists() && !HasError(errors, "Agent:AllowedHosts"))
        {
            foreach (var raw in ReadList(hosts))
                if (!HostPolicy.TryNormalize(raw, out _))
                    errors.Add(HostPolicy.ErrorFor(raw));
        }

        return errors.Count == 0 ? null : string.Join("; ", errors.Distinct());
    }

    private static bool HasError(List<string> errors, string key) =>
        errors.Any(e => e.StartsWith(key + " ", StringComparison.Ordinal));

    // Igual que Program.cs (Get<string[]>()): un array vacío o un escalar no son error y caen en el default.
    private static string?[] ReadList(IConfigurationSection section)
    {
        try { return section.Get<string[]>() ?? []; }
        catch (InvalidOperationException) { return []; }
    }

    private static bool TryGet<T>(IConfigurationSection agent, string key, List<string> errors, out T value)
    {
        value = default!;
        var section = agent.GetSection(key);
        if (!section.Exists() || HasError(errors, "Agent:" + key)) return false;
        try
        {
            value = section.Get<T>()!;
            return section.Value is not null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return false; // ya informado por CheckTypes
        }
    }

    private static void CheckTypes(IConfiguration config, string prefix, Type type, List<string> errors, int depth)
    {
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.SetMethod is not { IsPublic: true }) continue;
            var key = prefix + ":" + prop.Name;
            var section = config.GetSection(key);
            if (!section.Exists()) continue;

            var t = prop.PropertyType;
            if (depth == 0 && IsOptionsClass(t))
            {
                // Sección anidada (VideoPostprocess, Recording): solo tipos; los valores los corrige Normalize().
                CheckTypes(config, key, t, errors, depth + 1);
                continue;
            }

            try
            {
                section.Get(t);
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException)
            {
                var underlying = Nullable.GetUnderlyingType(t) ?? t;
                var shown = Cut(section.Value ?? "");
                if (underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(double))
                    errors.Add($"{key} no es un número: '{shown}'");
                else if (underlying == typeof(bool))
                    errors.Add($"{key} tiene que ser true o false: '{shown}'");
                else
                    errors.Add($"{key} no tiene un valor válido: '{shown}'");
            }
        }
    }

    private static bool IsOptionsClass(Type t) =>
        t.IsClass && t != typeof(string) && !t.IsArray &&
        t.Namespace == typeof(AgentOptions).Namespace;

    private static string Cut(string value) => value.Length <= MaxValueChars ? value : value[..MaxValueChars];
}
