using Factum.Backend.Models;

namespace Factum.Backend.Services.Catalogs;

/// <summary>
/// Único lugar del servidor que dice qué catálogos existen y de qué campos del caso se alimentan
/// (SDD §3.4, D9). Espejo en el cliente: <c>FIELD_CATALOG</c> de <c>client/src/lib/catalogs.ts</c>.
/// </summary>
public static class CatalogDefinitions
{
    public const string Destinatarios = "destinatarios";
    public const string Partes = "partes";
    public const string Profesiones = "profesiones";
    public const string TiposDispositivo = "tipos_dispositivo";
    public const string DefaultTipoDispositivo = "Teléfono celular";

    /// <summary>Catálogo → campos del caso que lo alimentan. Sumar un catálogo = una línea acá + FIELD_CATALOG en el cliente.</summary>
    public static readonly IReadOnlyDictionary<string, Func<Case, IEnumerable<string>>> Sources =
        new Dictionary<string, Func<Case, IEnumerable<string>>>(StringComparer.Ordinal)
        {
            [Destinatarios] = c => [c.NombreTribunal],
            [Partes] = c => [c.ParteDenunciante, c.ParteDenunciada, c.NombreProponente],
            [Profesiones] = c => [c.ProfesionProponente],
            [TiposDispositivo] = c => [c.TipoDispositivo],
        };

    public static bool Exists(string? id) => id is not null && Sources.ContainsKey(id);
    public static IEnumerable<string> Ids => Sources.Keys;
}
