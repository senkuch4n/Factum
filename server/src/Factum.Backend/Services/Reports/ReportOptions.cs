namespace Factum.Backend.Services.Reports;

/// <summary>
/// Sección <c>Report</c> de la config. Como <c>Branding</c>, los valores reales del estudio
/// (domicilio constituido, textos propios) van en <c>appsettings.Local.json</c> (ignorado por
/// git) o en variables de entorno <c>Report__*</c>. Ver README, "Informe pericial: configuración".
/// </summary>
public sealed class ReportOptions
{
    /// <summary>Zona IANA de la fecha/hora de la inspección.</summary>
    public string TimeZone { get; set; } = "America/Argentina/Buenos_Aires";

    /// <summary>Domicilio constituido del perito. Vacío = la frase se omite.</summary>
    public string DomicilioConstituido { get; set; } = "";

    /// <summary>
    /// Cifra el ZIP de evidencia con AES-256 (WinZip AE-2) y la contraseña generada para el caso.
    /// Default <c>true</c>. Con <c>false</c> el ZIP sale sin cifrar y sin contraseña (se loguea un
    /// warning al arrancar). Variable de entorno: <c>Report__EncryptZip</c>.
    /// </summary>
    public bool EncryptZip { get; set; } = true;

    /// <summary>Reemplazo por estudio de los textos por defecto. Vacío = default versionado.</summary>
    public ReportDefaultTextsOptions DefaultTexts { get; set; } = new();
}

public sealed class ReportDefaultTextsOptions
{
    public string OperacionesRealizadas { get; set; } = "";
    public string AseguramientoEvidencia { get; set; } = "";
    public string NotasTecnicas { get; set; } = "";
    public string Reserva { get; set; } = "";
}
