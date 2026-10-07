using MongoDB.Bson.Serialization.Attributes;

namespace Factum.Backend.Models;

/// <summary>
/// Valor sugerible de un catálogo del perito (colección <c>catalog_entries</c>,
/// formulario-caso-catalogos). El caso guarda texto: editar o borrar una entrada no cambia
/// ningún caso.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class CatalogEntry
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>DNI del perito dueño (mismo criterio que expert_profiles._id y cases.Officer.Dni).</summary>
    public string OwnerDni { get; set; } = string.Empty;
    /// <summary>Id del catálogo: "destinatarios" | "partes" | "profesiones" | "tipos_dispositivo".</summary>
    public string Catalog { get; set; } = string.Empty;
    /// <summary>Texto que se sugiere y se copia al caso (CatalogText.CleanValue).</summary>
    public string Value { get; set; } = string.Empty;
    /// <summary>Clave de deduplicación (CatalogText.NormalizeKey). Única por OwnerDni + Catalog.</summary>
    public string NormalizedKey { get; set; } = string.Empty;
    public int UseCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Marca de siembra (colección <c>catalog_seeds</c>): un documento por perito, <c>_id</c> = DNI.
/// Si existe, ese perito ya se sembró y no se vuelve a sembrar nunca.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class CatalogSeed
{
    [BsonId] public string Id { get; set; } = string.Empty; // DNI del perito
    public DateTime SeededAt { get; set; } = DateTime.UtcNow;
}
