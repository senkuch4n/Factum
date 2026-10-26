using Factum.Agent.Common;

namespace Factum.Agent.Services.Extraction;

/// <summary>
/// Nombre de la carpeta de una extracción (extraccion-logica-android §5.1):
/// <c>Factum_Extraccion_&lt;Marca&gt;-&lt;Modelo&gt;_&lt;yyyyMMdd-HHmmss&gt;</c>, con
/// <see cref="AgentFileNames.Sanitize"/> por parte. Si ya existe, suma <c>_2</c>, <c>_3</c>… Puro: el chequeo de
/// existencia se inyecta como predicado.
/// </summary>
public static class ExtractionFolderNames
{
    /// <summary>Nombre base, con la hora LOCAL de la PC (la que el perito ve en el nombre).</summary>
    public static string Build(string? manufacturer, string? model, DateTime localTime)
    {
        // AgentFileNames.Sanitize nunca devuelve vacío (cae en "caso"), que no describe un equipo: por eso el
        // default de dispositivo/android se decide sobre el texto crudo antes de sanear.
        var marca = string.IsNullOrWhiteSpace(manufacturer) ? "dispositivo" : AgentFileNames.Sanitize(manufacturer);
        var modelo = string.IsNullOrWhiteSpace(model) ? "android" : AgentFileNames.Sanitize(model);
        var stamp = localTime.ToString("yyyyMMdd-HHmmss");
        return $"Factum_Extraccion_{marca}-{modelo}_{stamp}";
    }

    /// <summary>
    /// Devuelve un nombre que no choca: <paramref name="baseName"/> si <paramref name="exists"/> dice que no está;
    /// si no, <c>_2</c>, <c>_3</c>… hasta encontrar uno libre.
    /// </summary>
    public static string Unique(string baseName, Func<string, bool> exists)
    {
        if (!exists(baseName)) return baseName;
        for (int n = 2; n < 10_000; n++)
        {
            var candidate = $"{baseName}_{n}";
            if (!exists(candidate)) return candidate;
        }
        // Muy improbable: usar un sufijo con marca de tiempo fina para no caer nunca.
        return $"{baseName}_{DateTime.UtcNow:fffffff}";
    }
}
