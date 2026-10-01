using System.Text;
using System.Text.Json;
using Factum.Backend.Common;
using Factum.Backend.Models;
using Microsoft.Extensions.Options;

namespace Factum.Backend.Services.Auth;

public sealed class MpfOptions
{
    public string MpfBaseUrl { get; set; } = string.Empty;
    public string MpfLoginPath { get; set; } = "/auth/login";
    public int MpfTimeoutSeconds { get; set; } = 10;
}

public sealed class MpfAuthProvider : IAuthProvider
{
    private readonly HttpClient _http;
    private readonly MpfOptions _opts;

    public MpfAuthProvider(IOptions<MpfOptions> opts, HttpClient http)
    {
        _opts = opts.Value;
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(_opts.MpfTimeoutSeconds);
        _http.BaseAddress = new Uri(_opts.MpfBaseUrl);
    }

    public string Mode => "mpf";

    public async Task<Result<User>> AuthenticateAsync(string dni, string username, string password,
        CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { dni, user = username, password });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(_opts.MpfLoginPath, content, ct);
        }
        catch (Exception ex)
        {
            return Result.Fail<User>($"No se pudo conectar a la API MPF: {ex.Message}");
        }

        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized
            or System.Net.HttpStatusCode.Forbidden)
            return Result.Fail<User>("Credenciales inválidas");

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            return Result.Fail<User>($"API MPF retornó {(int)response.StatusCode}: {body[..Math.Min(body.Length, 200)]}");
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
        var sigla = root.TryGetProperty("sigla", out var s) ? s.GetString() : string.Empty;

        if (string.IsNullOrEmpty(name))
            return Result.Fail<User>("API MPF no retornó nombre del funcionario");

        return Result.Ok(new User { Dni = dni, Name = name, Sigla = sigla ?? string.Empty });
    }
}
