using System.Diagnostics;

namespace Factum.Agent.Services;

/// <summary>
/// "Mostrar en carpeta" (zip-local-informe-servidor §6.3): abre el administrador de archivos con
/// el ZIP seleccionado, sin esperar a que termine. Siempre con <c>ArgumentList</c> (sin shell).
/// </summary>
public interface IFolderReveal
{
    void Reveal(string filePath);
}

public sealed class FolderReveal(ILogger<FolderReveal> log) : IFolderReveal
{
    public void Reveal(string filePath)
    {
        var psi = BuildStartInfo(filePath, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS());
        try
        {
            using var _ = Process.Start(psi);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "No se pudo abrir el administrador de archivos para {Path}", filePath);
            throw;
        }
    }

    /// <summary>Windows: <c>explorer.exe /select,"&lt;zip&gt;"</c>; macOS: <c>open -R</c>; Linux: <c>xdg-open &lt;dir&gt;</c>.</summary>
    internal static ProcessStartInfo BuildStartInfo(string filePath, bool isWindows, bool isMacOS)
    {
        var psi = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true };
        if (isWindows)
        {
            psi.FileName = "explorer.exe";
            // explorer espera "/select,<ruta>" como un único argumento.
            psi.ArgumentList.Add($"/select,{filePath}");
        }
        else if (isMacOS)
        {
            psi.FileName = "open";
            psi.ArgumentList.Add("-R");
            psi.ArgumentList.Add(filePath);
        }
        else
        {
            psi.FileName = "xdg-open";
            psi.ArgumentList.Add(Path.GetDirectoryName(filePath) ?? filePath);
        }
        return psi;
    }
}
