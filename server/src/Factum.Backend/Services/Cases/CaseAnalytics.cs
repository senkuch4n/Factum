using System.Globalization;
using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Models;

namespace Factum.Backend.Services.Cases;

/// <summary>
/// Agregaciones de solo lectura del dashboard del perito (dashboard-kpis-tendencias y
/// dashboard-breakdown). Ambas reusan <c>_repo.ListByOfficerAsync</c> (filtra por Officer.Dni, ya
/// proyecta afuera report_texts/zip_password/pending) y agregan EN MEMORIA: no tocan Mongo, no
/// crean índices ni escriben nada.
/// </summary>
public sealed partial class CaseService
{
    // ── dashboard-kpis-tendencias: GET /api/cases/stats ────────────────────────

    public async Task<CaseStatsResponse> GetStatsAsync(string officerDni, int tzOffsetMinutes,
        CancellationToken ct = default)
    {
        var cases = await _repo.ListByOfficerAsync(officerDni, ct);

        var total = cases.Count;
        var draft = cases.Count(c => c.Status == CaseStatus.Draft);
        var generating = cases.Count(c => c.Status == CaseStatus.Generating);
        var completed = cases.Count(c => c.Status == CaseStatus.Completed);
        var errored = cases.Count(c => c.Status == CaseStatus.Error);

        var completionRate = total == 0 ? 0.0 : (double)completed / total;

        // Promedio de cierre: solo completados con generated_at (los legacy sin fecha se excluyen).
        var closeTimes = cases
            .Where(c => c.Status == CaseStatus.Completed && c.GeneratedAt is not null)
            .Select(c => (c.GeneratedAt!.Value - c.CreatedAt).TotalSeconds)
            .ToList();
        double? avgCloseSeconds = closeTimes.Count == 0 ? null : closeTimes.Average();

        // Tendencia mensual en hora local del perito (D2/D5-A): 12 meses incluyendo el actual.
        var nowLocal = DateTime.UtcNow.AddMinutes(tzOffsetMinutes);
        var months = new List<string>(12);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 11; i >= 0; i--)
        {
            var key = nowLocal.AddMonths(-i).ToString("yyyy-MM", CultureInfo.InvariantCulture);
            months.Add(key);
            counts[key] = 0;
        }
        foreach (var c in cases)
        {
            var key = c.CreatedAt.AddMinutes(tzOffsetMinutes).ToString("yyyy-MM", CultureInfo.InvariantCulture);
            if (counts.ContainsKey(key)) counts[key]++;   // fuera del rango de 12 meses → se descarta
        }
        var monthly = months.Select(m => new MonthlyCount(m, counts[m])).ToList();

        return new CaseStatsResponse(
            total,
            new CaseStatusCounts(draft, generating, completed, errored),
            completionRate,
            avgCloseSeconds,
            monthly);
    }

    // ── dashboard-breakdown: GET /api/cases/breakdown ──────────────────────────

    public const string SinEspecificarKey = "__sin_especificar__";
    public const string SinEspecificarLabel = "Sin especificar";
    public const string OtrasKey = "__otras__";
    public const string OtrasLabel = "Otras";
    private const int BreakdownTopN = 8;

    private static readonly string[] BreakdownDimensions =
        ["platform", "status", "caratula", "ambito_causa"];

    public async Task<Result<BreakdownResponse>> BreakdownAsync(string officerDni, string? dimension,
        DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var dim = (dimension ?? string.Empty).Trim();
        if (!BreakdownDimensions.Contains(dim, StringComparer.Ordinal))
            return Result.Invalid<BreakdownResponse>(
                "dimension tiene que ser platform, status, caratula o ambito_causa");
        if (from is { } f && to is { } t && t < f)
            return Result.Invalid<BreakdownResponse>("to no puede ser anterior a from");

        var cases = await _repo.ListByOfficerAsync(officerDni, ct);

        // Filtro de fechas en UTC, borde superior inclusivo por día (D5b).
        DateTime? fromUtc = from is { } fd
            ? new DateTime(fd.Year, fd.Month, fd.Day, 0, 0, 0, DateTimeKind.Utc)
            : null;
        DateTime? toExclUtc = to is { } td
            ? new DateTime(td.Year, td.Month, td.Day, 0, 0, 0, DateTimeKind.Utc).AddDays(1)
            : null;
        var filtered = cases.Where(c =>
            (fromUtc is null || c.CreatedAt >= fromUtc.Value) &&
            (toExclUtc is null || c.CreatedAt < toExclUtc.Value)).ToList();

        long total = filtered.Count;
        var buckets = dim switch
        {
            "platform" => FixedBuckets(filtered,
                c => string.IsNullOrWhiteSpace(c.Device?.Platform) ? "android" : c.Device!.Platform,
                [("android", "Android"), ("ios", "iOS")]),
            "status" => FixedBuckets(filtered,
                c => CaseStatusKey(c.Status),
                [("draft", "Borradores"), ("generating", "Generando"),
                 ("completed", "Completados"), ("error", "Con error")]),
            "caratula" => FreeTextBuckets(filtered, c => c.Caratula),
            _ => FreeTextBuckets(filtered, c => c.AmbitoCausa),   // ambito_causa
        };

        return Result.Ok(new BreakdownResponse(dim, total, buckets));
    }

    private static string CaseStatusKey(CaseStatus s) => s switch
    {
        CaseStatus.Draft => "draft",
        CaseStatus.Generating => "generating",
        CaseStatus.Completed => "completed",
        CaseStatus.Error => "error",
        _ => "draft",
    };

    // platform/status: buckets fijos SIEMPRE presentes (incluso en 0), en el orden del catálogo.
    private static List<BreakdownBucket> FixedBuckets(List<Case> cases, Func<Case, string> keyOf,
        (string Key, string Label)[] fixedKeys)
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var c in cases)
        {
            var k = keyOf(c);
            counts[k] = counts.GetValueOrDefault(k) + 1;
        }
        return fixedKeys
            .Select(fk => new BreakdownBucket(fk.Key, fk.Label, counts.GetValueOrDefault(fk.Key)))
            .ToList();
    }

    // caratula/ambito_causa: texto libre. Vacío → "Sin especificar" (bucket propio, ordenado por su
    // count como cualquier otro). Top N = 8; el resto se acumula en "Otras" (D5a). Solo count > 0.
    private static List<BreakdownBucket> FreeTextBuckets(List<Case> cases, Func<Case, string> valueOf)
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var c in cases)
        {
            var raw = (valueOf(c) ?? string.Empty).Trim();
            var key = raw.Length == 0 ? SinEspecificarKey : raw;
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        // "Sin especificar" va SIEMPRE como bucket propio (D5a): no compite por un lugar del Top N
        // ni cae en "Otras". Se separa y se reinserta por su count junto al resto.
        counts.Remove(SinEspecificarKey, out var sinEspecificar);

        // Top N = 8 sobre las categorías reales; el resto se acumula en "Otras".
        var ordered = counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .ToList();

        var real = new List<BreakdownBucket>();
        long otras = 0;
        foreach (var kv in ordered)
        {
            if (real.Count < BreakdownTopN)
                real.Add(new BreakdownBucket(kv.Key, LabelFor(kv.Key), kv.Value));
            else
                otras += kv.Value;
        }

        // Juntar las reales (ya topeadas) con "Sin especificar" (si count > 0) y ordenar todo por
        // count desc; "Otras" queda al final.
        var combined = new List<BreakdownBucket>(real);
        if (sinEspecificar > 0)
            combined.Add(new BreakdownBucket(SinEspecificarKey, SinEspecificarLabel, sinEspecificar));
        var result = combined
            .OrderByDescending(b => b.Count)
            .ThenBy(b => b.Key, StringComparer.Ordinal)
            .ToList();
        if (otras > 0) result.Add(new BreakdownBucket(OtrasKey, OtrasLabel, otras));
        return result;
    }

    private static string LabelFor(string key) => key switch
    {
        SinEspecificarKey => SinEspecificarLabel,
        OtrasKey => OtrasLabel,
        _ => key,   // el label de una carátula/ámbito libre es su propio texto
    };
}
