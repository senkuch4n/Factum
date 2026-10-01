using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Models;

namespace Factum.Backend.Services.Support;

/// <summary>
/// Implementación con el soporte apagado: no tiene <see cref="HttpClient"/> ni llama a nadie.
/// Es una red de seguridad: <c>RequireSupportEnabledFilter</c> corta antes con 404.
/// </summary>
public sealed class DisabledSupportService : ISupportService
{
    public Task<Result<ReportarProblemaResponse>> ReportarProblemaAsync(
        User officer, ReportarProblemaRequest request, CancellationToken ct) =>
        Task.FromResult(Result.Fail<ReportarProblemaResponse>(SupportSettings.DisabledMessage));

    public Task<Result<List<MiTokenDto>>> ListarMisReportesAsync(User officer, CancellationToken ct) =>
        Task.FromResult(Result.Fail<List<MiTokenDto>>(SupportSettings.DisabledMessage));

    public Task<Result<bool>> CalificarReporteAsync(
        User officer, int idToken, CalificarTokenRequest request, CancellationToken ct) =>
        Task.FromResult(Result.Fail<bool>(SupportSettings.DisabledMessage));

    public Task<Result<FaroSsoLinkResponse>> ObtenerLinkFaroAsync(User officer, CancellationToken ct) =>
        Task.FromResult(Result.Fail<FaroSsoLinkResponse>(SupportSettings.DisabledMessage));
}
