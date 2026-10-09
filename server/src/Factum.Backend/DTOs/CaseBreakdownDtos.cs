namespace Factum.Backend.DTOs;

// dashboard-breakdown: respuesta de GET /api/cases/breakdown. JSON snake_case_lower (Program.cs):
// dimension, total, buckets[] { key, label, count }.

public sealed record BreakdownBucket(string Key, string Label, long Count);

public sealed record BreakdownResponse(string Dimension, long Total, IReadOnlyList<BreakdownBucket> Buckets);
