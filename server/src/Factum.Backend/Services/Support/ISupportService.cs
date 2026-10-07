using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Models;

namespace Factum.Backend.Services.Support;

public interface ISupportService
{
    Task<Result<ReportarProblemaResponse>> ReportarProblemaAsync(
        User officer, ReportarProblemaRequest request, CancellationToken ct);

    Task<Result<List<MiTokenDto>>> ListarMisReportesAsync(User officer, CancellationToken ct);

    Task<Result<bool>> CalificarReporteAsync(
        User officer, int idToken, CalificarTokenRequest request, CancellationToken ct);

    // Genera el enlace para que el oficial entre a Faro ya autenticado, sin
    // volver a poner su contraseña ahí (comparten la misma identidad (DNI)).
    Task<Result<FaroSsoLinkResponse>> ObtenerLinkFaroAsync(User officer, CancellationToken ct);
}
