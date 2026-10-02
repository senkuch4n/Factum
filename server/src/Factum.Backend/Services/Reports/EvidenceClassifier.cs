namespace Factum.Backend.Services.Reports;

public enum EvidenceClass { IdentityPhoto, Screenshot, Recording, DevicePull, Other }

/// <summary>
/// Clasificación de un archivo de evidencia del caso. Repite el criterio del cliente
/// (<c>CaptureStep.fileType</c>, <c>GenerateStep</c>). Las reglas se evalúan en este orden.
/// Los artefactos generados (<see cref="ReportService.IsGeneratedArtifact"/>) se filtran antes.
/// </summary>
public static class EvidenceClassifier
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg"];
    private static readonly string[] VideoExtensions = [".mp4", ".mkv", ".mov", ".avi", ".webm"];

    public static EvidenceClass Classify(string fileName, bool hasFileSource)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        if (fileName.Contains("foto_funcionario", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("foto_denunciante", StringComparison.OrdinalIgnoreCase))
            return EvidenceClass.IdentityPhoto;

        if ((fileName.Contains("screenshot", StringComparison.OrdinalIgnoreCase) ||
             fileName.Contains("captura", StringComparison.OrdinalIgnoreCase)) &&
            ImageExtensions.Contains(ext))
            return EvidenceClass.Screenshot;

        if (VideoExtensions.Contains(ext))
            return EvidenceClass.Recording;

        if (hasFileSource || fileName.StartsWith("device_pull_", StringComparison.OrdinalIgnoreCase))
            return EvidenceClass.DevicePull;

        return EvidenceClass.Other;
    }
}
