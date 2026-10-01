using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Microsoft.Extensions.Options;

namespace Factum.Backend.Services.Support;

public sealed class FaroIntegrationOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ServiceKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 10;

    // URL pública del frontend de Faro (no del backend) — a donde se manda al
    // oficial con el código de SSO.
    public string FrontendUrl { get; set; } = string.Empty;
}

// Le pasa al backend de Faro (sistema-gestion-de-tokens) los problemas que
// reportan los oficiales desde Factum, para que se abra un token de
// soporte del GFD sin que el oficial necesite loguearse ahí. Factum y Faro
// comparten la identidad de MPF — el Dni es la llave real, Faro resuelve (o
// provisiona) el mismo Usuario que vería si entrara directo a Faro.
public sealed class SupportService : ISupportService
{
    private const string SistemaOrigen = "Factum";

    // Faro serializa en camelCase (default de ASP.NET Core) — distinto del
    // snake_case que usa esta API — así que estas llamadas usan sus propias
    // opciones en vez de las globales de la app.
    private static readonly JsonSerializerOptions FaroJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly FaroIntegrationOptions _opts;

    public SupportService(IOptions<FaroIntegrationOptions> opts, HttpClient http)
    {
        _opts = opts.Value;
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(_opts.TimeoutSeconds);
        _http.BaseAddress = new Uri(_opts.BaseUrl);
    }

    public async Task<Result<ReportarProblemaResponse>> ReportarProblemaAsync(
        User officer, ReportarProblemaRequest request, CancellationToken ct)
    {
        var descripcion = request.Descripcion?.Trim() ?? string.Empty;
        if (descripcion.Length < 10)
            return Result.Fail<ReportarProblemaResponse>("La descripción debe tener al menos 10 caracteres");

        var payload = new
        {
            sistemaOrigen = SistemaOrigen,
            categoria = request.Categoria,
            descripcion,
            dni = officer.Dni,
            nombreCompleto = officer.Name,
            sigla = officer.Sigla,
            numeroInterno = request.NumeroInterno,
            telefono = request.Telefono,
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "/integraciones/tokens")
        {
            Content = JsonContent.Create(payload, options: FaroJsonOptions),
        };
        AddServiceKey(req);

        var (ok, response, error) = await SendAsync(req, ct);
        if (!ok) return Result.Fail<ReportarProblemaResponse>(error!);

        var creado = await response!.Content.ReadFromJsonAsync<FaroTokenCreado>(FaroJsonOptions, ct);
        if (creado is null)
            return Result.Fail<ReportarProblemaResponse>("Faro no devolvió una respuesta válida");

        return Result.Ok(new ReportarProblemaResponse(creado.NumeroToken));
    }

    public async Task<Result<List<MiTokenDto>>> ListarMisReportesAsync(User officer, CancellationToken ct)
    {
        var query = $"dni={HttpUtility.UrlEncode(officer.Dni)}";

        using var req = new HttpRequestMessage(HttpMethod.Get, $"/integraciones/tokens?{query}");
        AddServiceKey(req);

        var (ok, response, error) = await SendAsync(req, ct);
        if (!ok) return Result.Fail<List<MiTokenDto>>(error!);

        var items = await response!.Content.ReadFromJsonAsync<List<FaroTokenListItem>>(FaroJsonOptions, ct)
            ?? [];

        return Result.Ok(items.Select(i => new MiTokenDto(
            i.IdToken, i.Servicio, i.Estado, i.Descripcion, i.DescripcionResolucion,
            i.FechaCreacion, i.Puntuacion, i.Comentario
        )).ToList());
    }

    public async Task<Result<bool>> CalificarReporteAsync(
        User officer, int idToken, CalificarTokenRequest request, CancellationToken ct)
    {
        if (request.Puntuacion is < 1 or > 5)
            return Result.Fail<bool>("La puntuación debe estar entre 1 y 5");

        var query = $"dni={HttpUtility.UrlEncode(officer.Dni)}";

        using var req = new HttpRequestMessage(HttpMethod.Post, $"/integraciones/tokens/{idToken}/calificacion?{query}")
        {
            Content = JsonContent.Create(new { puntuacion = request.Puntuacion, comentario = request.Comentario }, options: FaroJsonOptions),
        };
        AddServiceKey(req);

        var (ok, _, error) = await SendAsync(req, ct);
        return ok ? Result.Ok(true) : Result.Fail<bool>(error!);
    }

    public async Task<Result<FaroSsoLinkResponse>> ObtenerLinkFaroAsync(User officer, CancellationToken ct)
    {
        var (nombre, apellido) = SplitName(officer.Name);
        var payload = new { dni = officer.Dni, nombre, apellido, sigla = officer.Sigla };

        using var req = new HttpRequestMessage(HttpMethod.Post, "/integraciones/sso")
        {
            Content = JsonContent.Create(payload, options: FaroJsonOptions),
        };
        AddServiceKey(req);

        var (ok, response, error) = await SendAsync(req, ct);
        if (!ok) return Result.Fail<FaroSsoLinkResponse>(error!);

        var body = await response!.Content.ReadFromJsonAsync<FaroSsoCode>(FaroJsonOptions, ct);
        if (body is null) return Result.Fail<FaroSsoLinkResponse>("Faro no devolvió un código de acceso válido");

        var url = $"{_opts.FrontendUrl.TrimEnd('/')}/sso?code={HttpUtility.UrlEncode(body.Code)}";
        return Result.Ok(new FaroSsoLinkResponse(url));
    }

    private static (string Nombre, string Apellido) SplitName(string fullName)
    {
        var partes = fullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return partes.Length > 1 ? (partes[0], partes[1]) : (fullName, string.Empty);
    }

    private void AddServiceKey(HttpRequestMessage req) => req.Headers.Add("X-Service-Key", _opts.ServiceKey);

    private async Task<(bool Ok, HttpResponseMessage? Response, string? Error)> SendAsync(
        HttpRequestMessage req, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(req, ct);
        }
        catch (Exception ex)
        {
            return (false, null, $"No se pudo contactar a Faro: {ex.Message}");
        }

        if (response.IsSuccessStatusCode) return (true, response, null);

        var motivo = await TryReadFaroError(response, ct);
        return (false, null, motivo ?? $"Faro respondió {(int)response.StatusCode}");
    }

    private static async Task<string?> TryReadFaroError(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<FaroErrorBody>(FaroJsonOptions, ct);
            return body?.Message;
        }
        catch
        {
            return null;
        }
    }

    private sealed record FaroTokenCreado(int NumeroToken);
    private sealed record FaroSsoCode(string Code);
    private sealed record FaroErrorBody(string? Message);
    private sealed record FaroTokenListItem(
        int IdToken, string Servicio, string Estado, string Descripcion,
        string? DescripcionResolucion, DateTime FechaCreacion, int? Puntuacion, string? Comentario);
}
