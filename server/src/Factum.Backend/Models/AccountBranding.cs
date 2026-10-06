using MongoDB.Bson.Serialization.Attributes;

namespace Factum.Backend.Models;

/// <summary>
/// Marca del informe de una cuenta (colección <c>account_brandings</c>, marca-por-cliente §4.1). Un
/// documento por cuenta, con <c>_id</c> = DNI del dueño (el mismo que <c>cases.Officer.Dni</c>). Las
/// imágenes van como binario dentro del documento. Si la cuenta nunca guardó una marca, no hay
/// documento y sus informes usan la <c>Branding</c> de <c>appsettings</c> (default de la instalación, D4).
/// </summary>
[BsonIgnoreExtraElements]
public sealed class AccountBranding
{
    /// <summary>DNI (7-8 dígitos).</summary>
    [BsonId] public string Id { get; set; } = "";
    /// <summary>Trim, ≤ 150. <c>""</c> = sin nombre.</summary>
    public string OrganizationName { get; set; } = "";
    /// <summary>≤ 6, cada una trim y ≤ 150, sin vacías.</summary>
    public List<string> ContactLines { get; set; } = [];
    /// <summary>null = sin logo.</summary>
    public AccountBrandingImage? Logo { get; set; }
    /// <summary>null = sin isotipo.</summary>
    public AccountBrandingImage? Isotype { get; set; }
    /// <summary><c>RRGGBB</c> en mayúsculas y sin '#'. <c>""</c> = verde de Factum.</summary>
    public string PrimaryColor { get; set; } = "";
    /// <summary>Ídem. <c>""</c> = tinte de Factum.</summary>
    public string AccentColor { get; set; } = "";
    /// <summary>UTC, truncado a ms.</summary>
    public DateTime CreatedAt { get; set; }
    /// <summary>UTC, truncado a ms. Token del control optimista.</summary>
    public DateTime UpdatedAt { get; set; }
    /// <summary>DNI de quien guardó.</summary>
    public string UpdatedBy { get; set; } = "";
}

/// <summary>Imagen de la marca (logo o isotipo). Nunca se guarda el nombre original ni metadatos de la subida.</summary>
[BsonIgnoreExtraElements]
public sealed class AccountBrandingImage
{
    /// <summary>BinData, ≤ 1 MiB.</summary>
    public byte[] Data { get; set; } = [];
    /// <summary><c>image/png</c> | <c>image/jpeg</c> (detectado por contenido).</summary>
    public string ContentType { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>Bytes.</summary>
    public long Size { get; set; }
    /// <summary>12 hex del SHA-256.</summary>
    public string Version { get; set; } = "";
}
