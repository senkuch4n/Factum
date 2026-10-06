using System.Security.Cryptography;
using Factum.Backend.DTOs;
using Factum.Backend.Infrastructure;
using Factum.Backend.Models;

namespace Factum.Backend.Services.Branding;

/// <summary>
/// Lo que llega en un guardado de la marca: la parte <c>metadata</c> y los bytes de las imágenes (si vinieron).
/// <paramref name="LogoTooBig"/>/<paramref name="IsotypeTooBig"/>: el archivo llegó pero pesa más de
/// <see cref="BrandingService.MaxLogoBytes"/> y no se leyó (§6.5.1); la validación lo informa en su turno
/// (orden de §5) como <c>image_too_big</c>.
/// </summary>
public sealed record BrandingSaveInput(BrandingSaveMetadata Metadata, byte[]? LogoBytes, byte[]? IsotypeBytes,
    bool LogoTooBig = false, bool IsotypeTooBig = false);

/// <summary>Valores ya normalizados (trim, colores <c>RRGGBB</c> o <c>""</c>, imágenes nuevas armadas).</summary>
public sealed record NormalizedBranding(
    string OrganizationName, List<string> ContactLines, string PrimaryColor, string AccentColor,
    ImageChange Logo, ImageChange Isotype)
{
    /// <summary>Sin nombre, sin líneas, colores de Factum y sin imágenes nuevas (DT7).</summary>
    public bool IsEmpty =>
        OrganizationName.Length == 0 && ContactLines.Count == 0 && PrimaryColor.Length == 0 &&
        AccentColor.Length == 0 && Logo.Action != BrandingImageAction.Replace &&
        Isotype.Action != BrandingImageAction.Replace;
}

/// <summary>Primer error de validación (un solo <c>field</c> por respuesta, DT9).</summary>
public sealed record BrandingFieldError(string Field, string Message, int? Index = null, double? Contrast = null)
{
    /// <summary>Extras del cuerpo de error: <c>index</c>, <c>contrast</c> y <c>min_contrast</c> si corresponden.</summary>
    public IReadOnlyDictionary<string, object?>? Extra()
    {
        if (Index is null && Contrast is null) return null;
        var extra = new Dictionary<string, object?>();
        if (Index is { } i) extra[BrandingErrors.IndexKey] = i;
        if (Contrast is { } c)
        {
            extra[BrandingErrors.ContrastKey] = c;
            extra[BrandingErrors.MinContrastKey] = BrandingErrors.MinContrast;
        }
        return extra;
    }
}

/// <summary>
/// Validación estricta + normalización de la marca que se guarda desde la web (marca-por-cliente §5). Pura.
/// A diferencia de <see cref="BrandingService"/> (appsettings, tolerante), acá todo se RECHAZA. La primera
/// falla corta, en el orden metadata → organization_name → contact_lines → primary_color → accent_color →
/// logo → isotype.
/// </summary>
public static class BrandingRules
{
    public const string ActionKeep = "keep";
    public const string ActionReplace = "replace";
    public const string ActionRemove = "remove";

    /// <summary>Contraste para mostrar: truncado (no redondeado) a 1 decimal. La comparación con 4.5 usa el valor sin truncar.</summary>
    public static double TruncatedContrast(double ratio) => Math.Floor(ratio * 10) / 10;

    public static (NormalizedBranding? Value, BrandingFieldError? Error) Validate(BrandingSaveInput input)
    {
        var m = input.Metadata;

        // 1. metadata: acciones de imagen.
        var logoAction = ParseAction(m.LogoAction);
        var isotypeAction = ParseAction(m.IsotypeAction);
        if (logoAction is null || isotypeAction is null) return Fail(BrandingErrors.FieldMetadata, BrandingErrors.MsgInvalidRequest);
        var logoHasFile = input.LogoBytes is not null || input.LogoTooBig;
        var isotypeHasFile = input.IsotypeBytes is not null || input.IsotypeTooBig;
        if ((logoHasFile && logoAction != BrandingImageAction.Replace) ||
            (isotypeHasFile && isotypeAction != BrandingImageAction.Replace))
            return Fail(BrandingErrors.FieldMetadata, BrandingErrors.MsgInvalidRequest);

        // 2. organization_name.
        var name = m.OrganizationName?.Trim() ?? "";
        if (name.Length > BrandingService.MaxNameLength)
            return Fail(BrandingErrors.FieldOrganizationName, BrandingErrors.MsgNameTooLong);
        if (HasControlChars(name))
            return Fail(BrandingErrors.FieldOrganizationName, BrandingErrors.MsgControlChars);

        // 3. contact_lines: largo y control por línea (índice en el array recibido), después la cantidad.
        var raw = m.ContactLines ?? [];
        var lines = new List<string>();
        for (var i = 0; i < raw.Count; i++)
        {
            var line = raw[i]?.Trim() ?? "";
            if (line.Length > BrandingService.MaxContactLineLength)
                return Fail(BrandingErrors.FieldContactLines, BrandingErrors.MsgContactLineTooLong, index: i);
            if (HasControlChars(line))
                return Fail(BrandingErrors.FieldContactLines, BrandingErrors.MsgControlChars, index: i);
            if (line.Length > 0) lines.Add(line);
        }
        if (lines.Count > BrandingService.MaxContactLines)
            return Fail(BrandingErrors.FieldContactLines, BrandingErrors.MsgTooManyContactLines);

        // 4. primary_color: contraste con blanco.
        var primary = "";
        if (!string.IsNullOrWhiteSpace(m.PrimaryColor))
        {
            if (BrandingColors.Normalize(m.PrimaryColor) is not { } hex)
                return Fail(BrandingErrors.FieldPrimaryColor, BrandingErrors.MsgColorFormat);
            var ratio = BrandingColors.ContrastWithWhite(hex);
            if (ratio < BrandingColors.MinPrimaryContrast)
            {
                var shown = TruncatedContrast(ratio);
                return Fail(BrandingErrors.FieldPrimaryColor, BrandingErrors.MsgPrimaryContrast(shown), contrast: shown);
            }
            primary = hex;
        }

        // 5. accent_color: contraste con la tinta.
        var accent = "";
        if (!string.IsNullOrWhiteSpace(m.AccentColor))
        {
            if (BrandingColors.Normalize(m.AccentColor) is not { } hex)
                return Fail(BrandingErrors.FieldAccentColor, BrandingErrors.MsgColorFormat);
            var ratio = BrandingColors.Contrast(hex, BrandingColors.InkColor);
            if (ratio < BrandingColors.MinAccentContrastWithInk)
            {
                var shown = TruncatedContrast(ratio);
                return Fail(BrandingErrors.FieldAccentColor, BrandingErrors.MsgAccentContrast(shown), contrast: shown);
            }
            accent = hex;
        }

        // 6-7. Imágenes.
        var (logo, logoError) = ImageFor(BrandingErrors.FieldLogo, logoAction.Value, input.LogoBytes, input.LogoTooBig);
        if (logoError is not null) return (null, logoError);
        var (isotype, isotypeError) = ImageFor(BrandingErrors.FieldIsotype, isotypeAction.Value, input.IsotypeBytes,
            input.IsotypeTooBig);
        if (isotypeError is not null) return (null, isotypeError);

        return (new NormalizedBranding(name, lines, primary, accent, logo!, isotype!), null);
    }

    /// <summary>
    /// Reglas de una imagen subida: vacía → formato; más de 1 MiB → <c>image_too_big</c> (el peso primero);
    /// no es PNG/JPEG → formato; algún lado fuera de 16-4096 → <c>image_size</c>.
    /// </summary>
    public static (AccountBrandingImage? Image, BrandingFieldError? Error) ValidateImage(string field, byte[] data)
    {
        if (data.Length == 0) return (null, new BrandingFieldError(field, BrandingErrors.MsgImageFormat));
        if (data.LongLength > BrandingService.MaxLogoBytes)
            return (null, new BrandingFieldError(field, BrandingErrors.MsgImageTooBig));
        if (!ImageProbe.TryDetect(data, out var contentType, out var w, out var h))
            return (null, new BrandingFieldError(field, BrandingErrors.MsgImageFormat));
        if (!SideOk(w) || !SideOk(h))
            return (null, new BrandingFieldError(field, BrandingErrors.MsgImageSize(w, h)));
        return (new AccountBrandingImage
        {
            Data = data,
            ContentType = contentType,
            Width = w,
            Height = h,
            Size = data.LongLength,
            Version = VersionOf(data),
        }, null);
    }

    /// <summary>12 hex (minúsculas) del SHA-256.</summary>
    public static string VersionOf(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data))[..12];

    public static bool SideOk(int side) => side >= BrandingService.MinLogoSide && side <= BrandingService.MaxLogoSide;

    /// <summary>"keep" | "replace" | "remove" (exacto) → acción; cualquier otra cosa (incluido null) → null.</summary>
    public static BrandingImageAction? ParseAction(string? raw) => raw switch
    {
        ActionKeep => BrandingImageAction.Keep,
        ActionReplace => BrandingImageAction.Replace,
        ActionRemove => BrandingImageAction.Remove,
        _ => null,
    };

    private static (ImageChange? Change, BrandingFieldError? Error) ImageFor(string field, BrandingImageAction action,
        byte[]? bytes, bool tooBig)
    {
        switch (action)
        {
            case BrandingImageAction.Keep: return (ImageChange.Keep, null);
            case BrandingImageAction.Remove: return (ImageChange.Remove, null);
        }
        if (tooBig) return (null, new BrandingFieldError(field, BrandingErrors.MsgImageTooBig));
        if (bytes is null) return (null, new BrandingFieldError(field, BrandingErrors.MsgImageMissing));
        var (image, error) = ValidateImage(field, bytes);
        return error is not null ? (null, error) : (new ImageChange(BrandingImageAction.Replace, image), null);
    }

    private static bool HasControlChars(string s)
    {
        foreach (var c in s)
            if (char.IsControl(c)) return true;
        return false;
    }

    private static (NormalizedBranding?, BrandingFieldError?) Fail(string field, string message, int? index = null,
        double? contrast = null) => (null, new BrandingFieldError(field, message, index, contrast));
}
