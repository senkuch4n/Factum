using Factum.Backend.Common;
using Factum.Backend.Models;

namespace Factum.Backend.Services.Auth;

/// <summary>
/// Resultado de un login correcto. <see cref="Role"/> y <see cref="MustChangePassword"/> no viajan
/// en <see cref="Models.User"/> porque <c>Case.Officer</c> lo persiste (usuarios-locales §3.3).
/// <see cref="PasswordChangedAt"/> solo en <c>local</c> (claim <c>pca</c>).
/// </summary>
public sealed record AuthenticatedUser(User User, string Role, bool MustChangePassword, DateTime? PasswordChangedAt);

public interface IAuthProvider
{
    string Mode { get; }
    Task<Result<AuthenticatedUser>> AuthenticateAsync(string dni, string? username, string password,
        CancellationToken ct = default);
}

/// <summary>Códigos y textos de error de autenticación (contrato §8.2 / §8.3).</summary>
public static class AuthErrors
{
    public const string InvalidCredentials = "invalid_credentials";
    public const string AccountLocked = "account_locked";
    public const string AccountSuspended = "account_suspended";
    public const string SessionRevoked = "session_revoked";
    public const string InvalidToken = "invalid_token";
    public const string Unauthenticated = "unauthenticated";
    public const string PasswordChangeRequired = "password_change_required";
    public const string NotAvailable = "not_available";
    public const string SuperadminRequired = "superadmin_required";

    public const string MsgInvalidCredentials = "DNI o contraseña incorrectos.";
    public static string MsgAccountLocked(int lockoutMinutes) =>
        $"Demasiados intentos fallidos. Probá de nuevo en {lockoutMinutes} minutos.";
    public const string MsgAccountSuspendedLogin = "Tu cuenta está suspendida. Comunicate con Factum para reactivarla.";
    public const string MsgAccountSuspendedSession = "Tu cuenta fue suspendida. Comunicate con Factum para reactivarla.";
    public const string MsgSessionEnded = "Tu sesión terminó. Volvé a ingresar.";
    public const string MsgUnauthenticated = "Iniciá sesión para continuar.";
    public const string MsgPasswordChangeRequired = "Tenés que cambiar tu contraseña antes de seguir.";
    public const string MsgNotAvailable = "El cambio de contraseña no está disponible en este modo.";
    public const string MsgSuperadminRequired = "Solo un superadmin puede hacer esto.";

    /// <summary>Error con <c>code</c> en <see cref="Result{T}.Details"/>.</summary>
    public static Result<T> Fail<T>(string code, string message, string? field = null)
    {
        var details = new Dictionary<string, object?> { ["code"] = code };
        if (field is not null) details["field"] = field;
        return Result.Fail<T>(ErrorKind.Failure, message, details);
    }

    /// <summary>El <c>code</c> de un Result fallido, o null.</summary>
    public static string? CodeOf<T>(Result<T> result) =>
        result.Details is { } d && d.TryGetValue("code", out var c) ? c as string : null;
}
