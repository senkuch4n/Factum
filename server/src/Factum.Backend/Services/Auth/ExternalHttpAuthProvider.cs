using System.Net;
using System.Text;
using System.Text.Json;
using Factum.Backend.Common;
using Factum.Backend.Models;

namespace Factum.Backend.Services.Auth;

/// <summary>
/// Proveedor de identidad HTTP externo genérico (<c>Auth:Mode=external</c>). Hace
/// <c>POST {BaseUrl}{LoginPath}</c> con los nombres de campo configurados y lee nombre y sigla
/// de la respuesta (con rutas con puntos). Los errores que ve el usuario son neutros; el detalle
/// técnico va al log. Nunca se loguean la contraseña, el body del request ni el DNI.
/// </summary>
public sealed class ExternalHttpAuthProvider : IAuthProvider
{
    private const string MsgInvalid = "Credenciales inválidas";
    private const string MsgUnavailable = "No se pudo conectar al servicio de autenticación. Intentá de nuevo en unos minutos.";
    private const string MsgUnexpected = "El servicio de autenticación respondió de forma inesperada.";

    private readonly HttpClient _http;
    private readonly ExternalAuthOptions _opts;
    private readonly Uri _loginUri;
    private readonly ILogger<ExternalHttpAuthProvider> _logger;

    public ExternalHttpAuthProvider(HttpClient http, AuthSettings settings, ILogger<ExternalHttpAuthProvider> logger)
    {
        _http = http;
        _opts = settings.External!;
        _loginUri = settings.ExternalLoginUri!;
        _logger = logger;
    }

    public string Mode => AuthModes.External;

    public async Task<Result<AuthenticatedUser>> AuthenticateAsync(string dni, string? username, string password,
        CancellationToken ct = default)
    {
        // username dejó de ser [Required] en el DTO (en local no se manda): se valida acá.
        if (string.IsNullOrEmpty(username))
            return Result.Fail<AuthenticatedUser>("Usuario requerido");

        var body = new Dictionary<string, string>
        {
            [_opts.Request.DniField] = dni,
            [_opts.Request.UserField] = username,
            [_opts.Request.PasswordField] = password,
        };
        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(_loginUri, content, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Login externo: no se pudo conectar ({Type}: {Message})", ex.GetType().Name, ex.Message);
            return Result.Fail<AuthenticatedUser>(MsgUnavailable);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Login externo: timeout de {TimeoutSeconds}s", _opts.TimeoutSeconds);
            return Result.Fail<AuthenticatedUser>(MsgUnavailable);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _logger.LogInformation("Login externo rechazado ({Status})", status);
                return Result.Fail<AuthenticatedUser>(MsgInvalid);
            }

            string text;
            try
            {
                text = await response.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException ||
                                       (ex is OperationCanceledException && !ct.IsCancellationRequested))
            {
                _logger.LogWarning("Login externo: error leyendo la respuesta ({Status}, {Type})", status, ex.GetType().Name);
                return Result.Fail<AuthenticatedUser>(MsgUnavailable);
            }

            if (status >= 500)
            {
                _logger.LogWarning("Login externo: el proveedor respondió {Status}: {Body}", status, Truncate(text));
                return Result.Fail<AuthenticatedUser>(MsgUnavailable);
            }
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Login externo: el proveedor respondió {Status}: {Body}", status, Truncate(text));
                return Result.Fail<AuthenticatedUser>(MsgUnexpected);
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(text);
            }
            catch (JsonException)
            {
                _logger.LogWarning("Login externo: respuesta no es un objeto JSON ({Status})", status);
                return Result.Fail<AuthenticatedUser>(MsgUnexpected);
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    _logger.LogWarning("Login externo: respuesta no es un objeto JSON ({Status})", status);
                    return Result.Fail<AuthenticatedUser>(MsgUnexpected);
                }

                var name = TryGetPath(root, _opts.Response.NameField, out var n) && n.ValueKind == JsonValueKind.String
                    ? n.GetString()?.Trim()
                    : null;
                if (string.IsNullOrEmpty(name))
                {
                    _logger.LogWarning("Login externo: la respuesta no trae '{NameField}'", _opts.Response.NameField);
                    return Result.Fail<AuthenticatedUser>(MsgUnexpected);
                }

                var sigla = string.Empty;
                if (!string.IsNullOrEmpty(_opts.Response.SiglaField) &&
                    TryGetPath(root, _opts.Response.SiglaField, out var s))
                {
                    sigla = s.ValueKind switch
                    {
                        JsonValueKind.String => s.GetString()?.Trim() ?? string.Empty,
                        JsonValueKind.Number => s.GetRawText(),
                        _ => string.Empty,
                    };
                }

                return Result.Ok(new AuthenticatedUser(
                    new User { Dni = dni, Name = name, Sigla = sigla }, UserRoles.Cliente, false, null));
            }
        }
    }

    /// <summary>Recorre una ruta con puntos (case-sensitive). Si algo intermedio no es objeto, no encuentra.</summary>
    private static bool TryGetPath(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        foreach (var segment in path.Split('.'))
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out var next))
            {
                value = default;
                return false;
            }
            value = next;
        }
        return true;
    }

    private static string Truncate(string s) => s.Length <= 200 ? s : s[..200];
}
