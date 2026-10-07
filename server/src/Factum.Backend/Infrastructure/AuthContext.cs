using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Factum.Backend.Models;

namespace Factum.Backend.Infrastructure;

/// <summary>
/// Rol y "debe cambiar la contraseña" de la sesión del request (usuarios-locales §6.1). Viaja aparte
/// de <see cref="User"/> porque <c>Case.Officer</c> persiste ese tipo. En dev/external es siempre
/// (<c>cliente</c>, <c>false</c>).
/// </summary>
public sealed record AuthSession(string Role, bool MustChangePassword);

public static class AuthContextKeys
{
    /// <summary><see cref="User"/> (Dni, Name, Sigla): sin cambios para los controllers.</summary>
    public const string User = "User";
    /// <summary><see cref="AuthSession"/>.</summary>
    public const string Session = "Session";
    /// <summary>Código de falla de autenticación para el challenge (§5.5).</summary>
    public const string FailureCode = "AuthFailureCode";
}

public static class AuthHttpContextExtensions
{
    public static AuthSession GetSession(this HttpContext ctx) =>
        ctx.Items[AuthContextKeys.Session] as AuthSession ?? new AuthSession(UserRoles.Cliente, false);
}

/// <summary>
/// JSON de los cuerpos que escriben el handler (401) y el gate (403) fuera de MVC: mismo texto que
/// devuelven los controllers (sin escapar tildes ni eñes).
/// </summary>
internal static class AuthJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public static Task WriteAsync(HttpResponse response, int status, IReadOnlyDictionary<string, string> body,
        CancellationToken ct)
    {
        response.StatusCode = status;
        response.ContentType = "application/json; charset=utf-8";
        return response.WriteAsync(JsonSerializer.Serialize(body, Options), ct);
    }
}
