using System.Text.Json;

namespace Factum.Agent.Common;

/// <summary>
/// Estado del <c>appsettings.Local.json</c> que usa Tatana (SDD tatana-instalador-autoupdate D9,
/// D-T16). Lo expone <c>GET /agent/state</c> como <c>local_config</c>.
/// </summary>
/// <param name="Path">Ruta absoluta del archivo (exista o no).</param>
/// <param name="Exists">El archivo existe.</param>
/// <param name="Loaded">Existe, es un objeto JSON válido y se agregó como fuente de configuración.</param>
/// <param name="Error">Motivo por el que se ignoró (JSON inválido o ilegible); null si está bien o no existe.</param>
/// <param name="OverridesAllowedOrigins">Define <c>Agent:AllowedOrigins</c>: las actualizaciones no cambian los orígenes.</param>
public sealed record LocalConfigStatus(string Path, bool Exists, bool Loaded, string? Error, bool OverridesAllowedOrigins);

/// <summary>
/// Valida el <c>appsettings.Local.json</c> ANTES de agregarlo a la configuración: un JSON roto no
/// tiene que impedir que Tatana arranque (si no, Electron entraría en un bucle de reinicios). Puro y
/// testeable: no registra nada.
/// </summary>
public static class LocalConfigInspector
{
    // Mismo tope razonable que cualquier config: evita leer un archivo enorme por error.
    private const long MaxBytes = 1024 * 1024;

    public static LocalConfigStatus Inspect(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        if (!File.Exists(full)) return new LocalConfigStatus(full, false, false, null, false);
        try
        {
            var info = new FileInfo(full);
            if (info.Length > MaxBytes)
                return new LocalConfigStatus(full, true, false, "el archivo es demasiado grande (más de 1 MB)", false);
            return InspectText(full, File.ReadAllText(full));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new LocalConfigStatus(full, true, false, "no se pudo leer: " + ex.Message, false);
        }
    }

    /// <summary><paramref name="text"/> null = el archivo no existe.</summary>
    internal static LocalConfigStatus InspectText(string path, string? text)
    {
        if (text is null) return new LocalConfigStatus(path, false, false, null, false);
        try
        {
            // Mismas tolerancias que el proveedor JSON de Microsoft.Extensions.Configuration.
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return new LocalConfigStatus(path, true, false, "el archivo no es un objeto JSON", false);
            return new LocalConfigStatus(path, true, true, null, DefinesAllowedOrigins(doc.RootElement));
        }
        catch (JsonException ex)
        {
            return new LocalConfigStatus(path, true, false, "JSON inválido: " + ex.Message, false);
        }
    }

    // Las claves de configuración son case-insensitive (Agent:AllowedOrigins == agent:allowedorigins).
    private static bool DefinesAllowedOrigins(JsonElement root)
    {
        foreach (var section in root.EnumerateObject())
        {
            if (!string.Equals(section.Name, "Agent", StringComparison.OrdinalIgnoreCase)) continue;
            if (section.Value.ValueKind != JsonValueKind.Object) continue;
            foreach (var key in section.Value.EnumerateObject())
                if (string.Equals(key.Name, "AllowedOrigins", StringComparison.OrdinalIgnoreCase))
                    return true;
        }
        return false;
    }
}
