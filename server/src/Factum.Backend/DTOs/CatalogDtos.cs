namespace Factum.Backend.DTOs;

// Catálogos de sugerencias (formulario-caso-catalogos, SDD §4.4). JSON snake_case_lower; las
// claves del diccionario de CatalogsResponse NO se transforman (son los ids de catálogo).

/// <summary>JSON: <c>{ id, value, use_count, last_used_at }</c>.</summary>
public sealed record CatalogEntryDto(string Id, string Value, int UseCount, DateTime LastUsedAt);

/// <summary>JSON: <c>{ catalogs: { destinatarios: [], partes: [], profesiones: [], tipos_dispositivo: [] } }</c>.</summary>
public sealed record CatalogsResponse(Dictionary<string, List<CatalogEntryDto>> Catalogs);

/// <summary>PUT /api/catalogs/{catalog}/entries/{id}: <c>{ value }</c>. string? sin [Required], como CaseDtos.</summary>
public sealed record UpdateCatalogEntryRequest(string? Value = null);
