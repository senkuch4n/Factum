using Factum.Backend.DTOs;
using Factum.Backend.Models;

namespace Factum.Backend.Services.Cases;

/// <summary>
/// Lógica pura del versionado de las secciones del informe (versionado-informe): snapshot,
/// de-duplicado (D4), poda conservando hitos (D3) y mapeo a DTO. Sin dependencias de I/O: la
/// escritura atómica la hace el repositorio.
/// </summary>
public static class ReportVersioning
{
    /// <summary>Foto de las ocho secciones + formato desde el <see cref="ReportTexts"/> a guardar.</summary>
    public static ReportTextSnapshot Snapshot(ReportTexts t) => new()
    {
        ObjetoInforme = t.ObjetoInforme,
        OperacionesRealizadas = t.OperacionesRealizadas,
        AseguramientoEvidencia = t.AseguramientoEvidencia,
        Resultados = t.Resultados,
        ValoracionTecnica = t.ValoracionTecnica,
        Conclusiones = t.Conclusiones,
        NotasTecnicas = t.NotasTecnicas,
        Reserva = t.Reserva,
        Formato = t.Formato,
    };

    /// <summary>
    /// De-dup (D4): true si el candidato es idéntico (ocho secciones + formato, comparación ordinal
    /// exacta) a la ÚLTIMA versión del historial (la de created_at más reciente). Si no hay historial,
    /// nunca es duplicado (la primera siempre se agrega).
    /// </summary>
    public static bool IsDuplicate(IReadOnlyList<ReportTextVersion> history, ReportTextSnapshot candidate)
    {
        if (history.Count == 0) return false;
        var last = history.OrderByDescending(v => v.CreatedAt).First();
        return SameTexts(last.Texts, candidate);
    }

    private static bool SameTexts(ReportTextSnapshot a, ReportTextSnapshot b) =>
        string.Equals(a.ObjetoInforme, b.ObjetoInforme, StringComparison.Ordinal) &&
        string.Equals(a.OperacionesRealizadas, b.OperacionesRealizadas, StringComparison.Ordinal) &&
        string.Equals(a.AseguramientoEvidencia, b.AseguramientoEvidencia, StringComparison.Ordinal) &&
        string.Equals(a.Resultados, b.Resultados, StringComparison.Ordinal) &&
        string.Equals(a.ValoracionTecnica, b.ValoracionTecnica, StringComparison.Ordinal) &&
        string.Equals(a.Conclusiones, b.Conclusiones, StringComparison.Ordinal) &&
        string.Equals(a.NotasTecnicas, b.NotasTecnicas, StringComparison.Ordinal) &&
        string.Equals(a.Reserva, b.Reserva, StringComparison.Ordinal) &&
        string.Equals(a.Formato ?? string.Empty, b.Formato ?? string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// Decide cómo se agrega la versión nueva (D3): si el historial está por debajo del tope, $push
    /// simple (menos payload); si ya llegó al tope, devuelve la lista PODADA (para un $set) que
    /// conserva la PRIMERA versión del caso, TODAS las "generate" (hitos forenses) y, de las
    /// restantes, las más recientes hasta completar <see cref="CaseService.MaxReportVersions"/>
    /// contando la nueva. Nunca se poda la recién agregada ni los hitos.
    /// </summary>
    public static (ReportTextVersion? Push, List<ReportTextVersion>? PrunedOverride) PlanAppend(
        IReadOnlyList<ReportTextVersion> history, ReportTextVersion incoming)
    {
        if (history.Count + 1 <= CaseService.MaxReportVersions)
            return (incoming, null);
        return (null, Prune(history, incoming));
    }

    private static List<ReportTextVersion> Prune(IReadOnlyList<ReportTextVersion> history,
        ReportTextVersion incoming)
    {
        // Orden cronológico ascendente de lo ya existente.
        var ordered = history.OrderBy(v => v.CreatedAt).ToList();

        // Siempre conservados: la primera del caso y todas las "generate".
        var keep = new List<ReportTextVersion>();
        var keepIds = new HashSet<string>(StringComparer.Ordinal);
        void Keep(ReportTextVersion v) { if (keepIds.Add(v.Id)) keep.Add(v); }

        if (ordered.Count > 0) Keep(ordered[0]);                                   // primera del caso
        foreach (var v in ordered)
            if (v.Trigger == ReportVersionTriggers.Generate) Keep(v);              // hitos forenses

        // De las restantes (ni primera ni generate), las más recientes hasta completar el tope,
        // dejando lugar para la nueva (que siempre entra).
        var remaining = ordered.Where(v => !keepIds.Contains(v.Id))
            .OrderByDescending(v => v.CreatedAt)
            .ToList();
        var budget = CaseService.MaxReportVersions - keep.Count - 1;   // -1 por la nueva
        if (budget > 0)
            foreach (var v in remaining.Take(budget)) Keep(v);

        // Reconstruir en orden cronológico ascendente + la nueva al final.
        var result = keep.OrderBy(v => v.CreatedAt).ToList();
        result.Add(incoming);
        return result;
    }

    /// <summary>
    /// Normaliza el trigger del PUT: null/""/"save" → "save"; "restore" → "restore"; cualquier otro
    /// valor → error de validación. restored_from solo se acepta con "restore". Devuelve false con
    /// el mensaje de error si algo no cuadra.
    /// </summary>
    public static bool TryNormalizeTrigger(string? raw, string? restoredFrom, out string trigger,
        out string? error)
    {
        error = null;
        var t = (raw ?? string.Empty).Trim();
        if (t.Length == 0 || t == ReportVersionTriggers.Save)
        {
            trigger = ReportVersionTriggers.Save;
            if (!string.IsNullOrEmpty(restoredFrom))
            {
                trigger = ReportVersionTriggers.Save;
                error = "restored_from solo es válido con trigger \"restore\"";
                return false;
            }
            return true;
        }
        if (t == ReportVersionTriggers.Restore)
        {
            trigger = ReportVersionTriggers.Restore;
            return true;
        }
        trigger = ReportVersionTriggers.Save;
        error = $"trigger inválido: tiene que ser \"{ReportVersionTriggers.Save}\" o \"{ReportVersionTriggers.Restore}\"";
        return false;
    }

    public static ReportTextVersionDto ToDto(ReportTextVersion v) => new(
        v.Id, v.CreatedAt, v.AuthorDni, v.AuthorName, v.Trigger, v.RestoredFrom,
        new ReportTextVersionTextsDto(
            v.Texts.ObjetoInforme, v.Texts.OperacionesRealizadas, v.Texts.AseguramientoEvidencia,
            v.Texts.Resultados, v.Texts.ValoracionTecnica, v.Texts.Conclusiones,
            v.Texts.NotasTecnicas, v.Texts.Reserva, v.Texts.Formato));

    /// <summary>
    /// Versión "generate" (D2) desde el report_texts vigente de un caso. Autor = Officer del caso.
    /// De-dup propio: null si es idéntica a la última (no se duplica un hito ya registrado igual).
    /// </summary>
    public static ReportTextVersion? GenerateVersionFor(Case cas)
    {
        var snapshot = Snapshot(cas.ReportTexts ?? new ReportTexts());
        if (IsDuplicate(cas.ReportTextVersions, snapshot)) return null;
        return new ReportTextVersion
        {
            Id = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow,
            AuthorDni = cas.Officer.Dni,
            AuthorName = cas.Officer.Name,
            Trigger = ReportVersionTriggers.Generate,
            Texts = snapshot,
        };
    }
}
