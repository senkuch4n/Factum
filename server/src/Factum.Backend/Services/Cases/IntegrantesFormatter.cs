using Factum.Backend.Services.Catalogs;

namespace Factum.Backend.Services.Cases;

/// <summary>
/// Frase de integrantes ("A, B y C") que se guarda en <c>IntegrantesTribunal</c> y lee el
/// informe (SDD §4.2). Tiene que dar idéntico que <c>joinIntegrantes</c> en
/// <c>client/src/lib/pericial.ts</c>.
/// </summary>
public static class IntegrantesFormatter
{
    /// <summary>Limpia cada ítem (CatalogText.CleanValue), descarta los vacíos y conserva el orden.</summary>
    public static List<string> Clean(IEnumerable<string?>? items)
    {
        var list = new List<string>();
        if (items is null) return list;
        foreach (var item in items)
        {
            var v = CatalogText.CleanValue(item);
            if (v.Length > 0) list.Add(v);
        }
        return list;
    }

    /// <summary>
    /// "", "A", "A y B", "A, B y C". Conector final " e " si el último ítem empieza con sonido /i/
    /// ("i…" o "hi" + no vocal / fin), " y " en el resto (D5, DP1 A). La entrada ya llega limpia.
    /// </summary>
    public static string Join(IReadOnlyList<string> items)
    {
        if (items.Count == 0) return string.Empty;
        if (items.Count == 1) return items[0];
        var last = items[^1];
        var connector = StartsWithISound(last) ? " e " : " y ";
        return string.Join(", ", items.Take(items.Count - 1)) + connector + last;
    }

    private static bool StartsWithISound(string value)
    {
        var k = CatalogText.NormalizeKey(value);
        if (k.StartsWith('i')) return true;
        if (k.StartsWith("hi", StringComparison.Ordinal))
            return k.Length == 2 || !IsVowel(k[2]);
        return false;
    }

    private static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u';
}
