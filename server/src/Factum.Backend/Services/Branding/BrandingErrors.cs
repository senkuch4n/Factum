using System.Globalization;
using Factum.Backend.Services.Admin;

namespace Factum.Backend.Services.Branding;

/// <summary>
/// Códigos, campos y textos de la marca por cuenta (marca-por-cliente §5.1 y contrato §8). Los textos
/// tienen que coincidir con <c>client/src/lib/branding.ts</c> (<c>BRANDING_MESSAGES</c>).
/// </summary>
public static class BrandingErrors
{
    // ── códigos (§6.5 / §8.1 BrandingErrorCode) ──────────────────────────────
    public const string ValidationFailed = AdminErrors.ValidationFailed;
    public const string StaleUpdate = AdminErrors.StaleUpdate;
    public const string UserNotFound = AdminErrors.UserNotFound;
    public const string NotAvailable = AdminErrors.NotAvailable;
    public const string ImageNotFound = "image_not_found";
    public const string RequestTooLarge = "request_too_large";

    // ── campos del error (BrandingField) ─────────────────────────────────────
    public const string FieldMetadata = "metadata";
    public const string FieldOrganizationName = "organization_name";
    public const string FieldContactLines = "contact_lines";
    public const string FieldPrimaryColor = "primary_color";
    public const string FieldAccentColor = "accent_color";
    public const string FieldLogo = "logo";
    public const string FieldIsotype = "isotype";

    // ── claves extra del cuerpo de error ─────────────────────────────────────
    public const string IndexKey = "index";
    public const string ContrastKey = "contrast";
    public const string MinContrastKey = "min_contrast";
    public const string MaxBytesKey = "max_bytes";

    /// <summary>Contraste mínimo que se informa en <c>min_contrast</c> (primario y acento).</summary>
    public const double MinContrast = 4.5;

    // ── textos (§5.1) ────────────────────────────────────────────────────────
    public const string MsgNameTooLong = "El nombre de la organización puede tener hasta 150 caracteres.";
    public const string MsgControlChars = "No puede tener saltos de línea ni caracteres de control.";
    public const string MsgContactLineTooLong = "Cada línea de contacto puede tener hasta 150 caracteres.";
    public const string MsgTooManyContactLines = "Podés cargar hasta 6 líneas de contacto.";
    public const string MsgColorFormat = "Ingresá un color en formato #RRGGBB.";
    public const string MsgImageFormat = "El archivo no es una imagen PNG ni JPEG.";
    public const string MsgImageTooBig = "La imagen supera el máximo de 1 MB.";
    public const string MsgImageMissing = "Falta el archivo de la imagen.";
    public const string MsgInvalidRequest = "La solicitud no es válida. Recargá e intentá de nuevo.";
    public const string MsgStaleBranding = "La marca se modificó desde otra sesión. Recargá para ver los cambios.";
    public const string MsgRequestTooLarge = "La marca supera el tamaño máximo permitido (2,5 MB en total).";
    public const string MsgBrandingImageNotFound = "La imagen no existe.";

    /// <summary><paramref name="contrast"/> ya truncado a un decimal (<see cref="BrandingRules.TruncatedContrast"/>).</summary>
    public static string MsgPrimaryContrast(double contrast) =>
        $"Contraste {FormatContrast(contrast)}:1 con blanco, mínimo 4.5:1.";

    /// <summary><paramref name="contrast"/> ya truncado a un decimal (<see cref="BrandingRules.TruncatedContrast"/>).</summary>
    public static string MsgAccentContrast(double contrast) =>
        $"Contraste {FormatContrast(contrast)}:1 con el texto, mínimo 4.5:1.";

    /// <summary>El <c>×</c> es U+00D7.</summary>
    public static string MsgImageSize(int w, int h) =>
        $"La imagen mide {w}×{h} px; cada lado tiene que estar entre 16 y 4096 px.";

    /// <summary>Un decimal, con punto (<c>2.1</c>).</summary>
    public static string FormatContrast(double contrast) => contrast.ToString("0.0", CultureInfo.InvariantCulture);
}
