namespace Factum.Backend.DTOs;

// dashboard-kpis-tendencias: respuesta de GET /api/cases/stats. JSON snake_case_lower (Program.cs).
// by_status es un objeto plano con 4 claves fijas (no un diccionario indexado por el enum): así el
// contrato es estable aunque cambie el orden del enum CaseStatus.

public sealed record CaseStatsResponse(
    int Total,
    CaseStatusCounts ByStatus,
    double CompletionRate,
    double? AvgCloseSeconds,
    List<MonthlyCount> Monthly);

public sealed record CaseStatusCounts(int Draft, int Generating, int Completed, int Error);

public sealed record MonthlyCount(string Month, int Count);
