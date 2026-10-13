namespace Factum.Backend.Services.Reports;

/// <summary>Datos de la portada que pueden crecer (informe-formato-fixes §6.4). Valores ya limpios.</summary>
internal sealed record CoverInput(string CausaRow, string Caratula, string PeritoRow,
    string OrganizationName, IReadOnlyList<string> ContactLines, bool HasLogo);

/// <summary>
/// Densidad de la portada (informe-formato-fixes §6.4, DA3): estimación pura de la altura (en
/// twips) de la ficha y del membrete del pie de portada, y elección de uno de tres niveles para
/// que la portada entre siempre en una página. Nivel 0 = la plantilla tal cual.
/// <list type="table">
/// <item>0: espaciador 1800, subtítulo after 1650, carátula 11 pt, contacto una línea por renglón 9 pt.</item>
/// <item>1: espaciador 600, subtítulo 600, carátula 10 pt.</item>
/// <item>2: espaciador 240, subtítulo 240, carátula 9 pt, contacto unido con " · " a 8 pt.</item>
/// </list>
/// Las constantes salen de renders con LibreOffice (calibración §11.3).
/// </summary>
internal static class ReportCoverLayout
{
    // ── Por nivel (índice = nivel) ──────────────────────────────────────────
    /// <summary>w:line exacto del primer párrafo (espaciador superior).</summary>
    internal static readonly string[] SpacerLine = ["1800", "600", "240"];
    /// <summary>w:after del subtítulo "Inspección técnica de dispositivo móvil".</summary>
    internal static readonly string[] SubtitleAfter = ["1650", "600", "240"];
    /// <summary>Tamaño de la carátula en medios puntos (11 / 10 / 9 pt).</summary>
    internal static readonly string[] CaratulaHalfPoints = ["22", "20", "18"];
    /// <summary>Tamaño del contacto unido del nivel 2 (8 pt).</summary>
    internal const string JoinedContactHalfPoints = "16";
    internal const string ContactSeparator = " · ";

    // ── Ficha ───────────────────────────────────────────────────────────────
    // Caracteres por línea de la celda de valor (6727 twips) en Arial 11/10/9 pt, y alto de línea
    // (interlineado 1,15) en twips.
    private static readonly int[] CaratulaCpl = [62, 68, 76];
    private static readonly int[] CaratulaLineH = [290, 264, 238];
    private const int RowCpl = 62;      // causa y perito: siempre 11 pt
    private const int RowLineH = 290;
    private const int CaratulaQuotes = 2; // “…” de la plantilla

    // ── Membrete (footer2) ──────────────────────────────────────────────────
    private const int MembreteBase = 60;          // after del nombre
    private const int OrgLineH = 242;             // Arial 10,5 pt negrita
    private const int OrgCplWithLogo = 52;        // celda de texto 5796 twips
    private const int OrgCplNoLogo = 78;          // ancho completo
    private const int ContactLineH = 207;         // Arial 9 pt
    private const int ContactCplWithLogo = 67;
    private const int ContactCplNoLogo = 100;
    private const int JoinedContactLineH = 184;   // Arial 8 pt
    private const double JoinedContactCplFactor = 1.12; // 9 pt → 8 pt
    private const int LogoMinHeight = 1418;       // caja del logo 2,5 cm

    // ── Capacidad ───────────────────────────────────────────────────────────
    internal const int Level0Capacity = 6000;
    internal const int Level1Capacity = 7800;
    /// <summary>Nivel 1 + lo que ahorran los espaciadores del nivel 2 ((600-240) × 2).</summary>
    internal const int Level2Capacity = 8520;

    internal static int ChooseLevel(CoverInput c)
    {
        if (EstimateTwips(c, 0) <= Level0Capacity) return 0;
        if (EstimateTwips(c, 1) <= Level1Capacity) return 1;
        return 2;
    }

    internal static bool FitsAtLevel(CoverInput c, int level) => EstimateTwips(c, level) <= level switch
    {
        0 => Level0Capacity,
        1 => Level1Capacity,
        _ => Level2Capacity,
    };

    internal static int EstimateTwips(CoverInput c, int level)
    {
        level = Math.Clamp(level, 0, 2);
        var ficha = Lines(c.CausaRow.Length, RowCpl) * RowLineH
                    + Lines(c.Caratula.Length + CaratulaQuotes, CaratulaCpl[level]) * CaratulaLineH[level]
                    + Lines(c.PeritoRow.Length, RowCpl) * RowLineH;
        return ficha + Membrete(c, level);
    }

    private static int Membrete(CoverInput c, int level)
    {
        var lines = c.ContactLines.Where(l => l.Length > 0).ToList();
        if (c.OrganizationName.Length == 0 && lines.Count == 0 && !c.HasLogo) return 0;

        var cplOrg = c.HasLogo ? OrgCplWithLogo : OrgCplNoLogo;
        var cplContact = c.HasLogo ? ContactCplWithLogo : ContactCplNoLogo;
        var total = MembreteBase + OrgLineH * Lines(c.OrganizationName.Length, cplOrg);
        if (level < 2)
            total += lines.Sum(l => ContactLineH * Lines(l.Length, cplContact));
        else if (lines.Count > 0)
            total += JoinedContactLineH * Lines(string.Join(ContactSeparator, lines).Length,
                (int)Math.Floor(cplContact * JoinedContactCplFactor));
        return c.HasLogo ? Math.Max(total, LogoMinHeight) : total;
    }

    // ceil(len / cpl); un texto vacío igual ocupa su renglón (la fila está en la plantilla).
    private static int Lines(int length, int cpl) => Math.Max(1, (length + cpl - 1) / cpl);
}
