namespace Factum.Backend.DTOs;

// marca-por-cliente §7 / contrato §8.1. Se serializan con JsonNamingPolicy.SnakeCaseLower (Program.cs).
// Los requests NO llevan [Required]: son nullables y el servicio valida (validation_failed + field).

/// <summary>
/// Imagen de la marca. <c>Url</c> es una ruta relativa al backend con <c>?v=&lt;version&gt;</c>; se pide con
/// <c>Authorization</c> (fetch → blob), nunca con <c>?token=</c>.
/// </summary>
public sealed record BrandingImageDto(string Url, string ContentType, int Width, int Height, long Size, string Version);

/// <summary>
/// Marca del informe de una cuenta. Con <c>Exists = false</c> todo va vacío o null, <c>ContactLines = []</c> y
/// <c>UpdatedAt = null</c>: los informes de esa cuenta usan la marca de la instalación (D4).
/// </summary>
public sealed record AccountBrandingDto(
    bool Exists,
    string OrganizationName,
    List<string> ContactLines,
    string? PrimaryColor,          // "#1F3A93" o null (= verde de Factum)
    string? AccentColor,           // "#E6ECFA" o null (= tinte de Factum)
    BrandingImageDto? Logo,
    BrandingImageDto? Isotype,
    DateTime? UpdatedAt,
    string? UpdatedBy,
    string? SuggestedOrganizationName); // solo con Exists = false, en local y si users.Organization tiene valor (D10)

public sealed record AccountBrandingResponse(AccountBrandingDto Branding);
public sealed record AccountBrandingSaveResponse(AccountBrandingDto Branding, bool Changed);

/// <summary>Parte <c>metadata</c> (JSON) del PUT multipart. Sin [Required]: el servicio valida.</summary>
public sealed record BrandingSaveMetadata(
    string? OrganizationName,
    List<string?>? ContactLines,
    string? PrimaryColor,
    string? AccentColor,
    string? LogoAction,            // "keep" | "replace" | "remove"
    string? IsotypeAction,
    DateTime? ExpectedUpdatedAt);
