using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Factum.Backend.Services.Auth;

namespace Factum.Backend.Services.Branding;

/// <summary>Marca con la que sale el informe de un caso (marca-por-cliente §6.2, D4/D8).</summary>
public interface IReportBrandingResolver
{
    /// <summary>Marca del informe de un caso cuyo dueño es <paramref name="ownerDni"/>. Nunca devuelve null.</summary>
    Task<BrandingSnapshot> ResolveAsync(string? ownerDni, CancellationToken ct = default);
}

/// <summary>
/// Resuelve la marca del DUEÑO del caso en el instante de la generación, sin caché (DT2):
/// <list type="bullet">
/// <item>DNI vacío o inválido → la marca de la instalación (sin consultar la base);</item>
/// <item>la cuenta nunca guardó marca → la marca de la instalación, entera (D4);</item>
/// <item>la cuenta tiene marca → SOLO la de la cuenta, sin mezclar campo por campo (D4-A).</item>
/// </list>
/// Una sola lectura por generación. Si Mongo falla, la excepción se propaga y la generación falla (DT3):
/// nunca sale un informe con la marca equivocada.
/// </summary>
public sealed class ReportBrandingResolver(
    IAccountBrandingRepository repo,
    IBrandingService installation,
    ILogger<ReportBrandingResolver> log) : IReportBrandingResolver
{
    public async Task<BrandingSnapshot> ResolveAsync(string? ownerDni, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(ownerDni) || !DniFormat.IsValid(ownerDni)) return installation.Current;

        var doc = await repo.FindAsync(ownerDni, ct);
        if (doc is null) return installation.Current;

        var name = doc.OrganizationName?.Trim() ?? "";
        return new BrandingSnapshot(
            name.Length == 0 ? null : name,
            (doc.ContactLines ?? []).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList().AsReadOnly(),
            ToLogo(doc.Logo, ownerDni, "logo"),
            ToLogo(doc.Isotype, ownerDni, "isotipo"),
            Primary(doc.PrimaryColor, ownerDni),
            Accent(doc.AccentColor, ownerDni));
    }

    // Defensivo: se validó al escribir, pero se vuelve a chequear. Nunca se loguean bytes.
    private BrandingLogo? ToLogo(AccountBrandingImage? img, string dni, string label)
    {
        if (img is null) return null;

        BrandingLogo? Ignore(string reason)
        {
            log.LogWarning("Marca de cuenta {Dni}: {Label} ignorado ({Reason})", dni, label, reason);
            return null;
        }

        var data = img.Data ?? [];
        if (data.Length == 0) return Ignore("sin datos");
        if (data.LongLength > BrandingService.MaxLogoBytes) return Ignore($"pesa {data.LongLength} bytes");
        if (!ImageProbe.TryDetect(data, out var contentType, out var w, out var h))
            return Ignore("no es un PNG ni un JPEG válido");
        if (!BrandingRules.SideOk(w) || !BrandingRules.SideOk(h)) return Ignore($"mide {w}×{h} px");

        var ext = contentType == "image/png" ? ".png" : ".jpg";
        var version = string.IsNullOrEmpty(img.Version) ? BrandingRules.VersionOf(data) : img.Version;
        return new BrandingLogo(data, contentType, ext, w, h, version);
    }

    private string Primary(string? raw, string dni)
    {
        if (string.IsNullOrEmpty(raw)) return BrandingColors.DefaultPrimary;
        if (BrandingColors.Normalize(raw) is { } hex &&
            BrandingColors.ContrastWithWhite(hex) >= BrandingColors.MinPrimaryContrast)
            return hex;
        log.LogWarning("Marca de cuenta {Dni}: color primario inválido; se usa el default", dni);
        return BrandingColors.DefaultPrimary;
    }

    private string Accent(string? raw, string dni)
    {
        if (string.IsNullOrEmpty(raw)) return BrandingColors.DefaultAccent;
        if (BrandingColors.Normalize(raw) is { } hex &&
            BrandingColors.Contrast(hex, BrandingColors.InkColor) >= BrandingColors.MinAccentContrastWithInk)
            return hex;
        log.LogWarning("Marca de cuenta {Dni}: color de acento inválido; se usa el default", dni);
        return BrandingColors.DefaultAccent;
    }
}
