using Factum.Backend.Services.Auth;
using Microsoft.AspNetCore.Authorization;

namespace Factum.Backend.Infrastructure;

/// <summary>Endpoints que se pueden usar con el cambio de contraseña pendiente (me, change-password).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowDuringPasswordChangeAttribute : Attribute;

/// <summary>
/// Impone el cambio obligatorio de contraseña (usuarios-locales §6.5, T6): con
/// <c>MustChangePassword = true</c>, todo endpoint <c>[Authorize]</c> sin
/// <see cref="AllowDuringPasswordChangeAttribute"/> responde 403 <c>password_change_required</c>.
/// Va después de <c>UseAuthorization()</c>.
/// </summary>
public sealed class PasswordChangeGateMiddleware(RequestDelegate next)
{
    internal static bool ShouldBlock(EndpointMetadataCollection? metadata, AuthSession? session) =>
        session?.MustChangePassword == true &&
        metadata is not null &&
        metadata.GetMetadata<IAuthorizeData>() is not null &&
        metadata.GetMetadata<IAllowAnonymous>() is null &&
        metadata.GetMetadata<AllowDuringPasswordChangeAttribute>() is null;

    public Task InvokeAsync(HttpContext ctx)
    {
        var session = ctx.Items[AuthContextKeys.Session] as AuthSession;
        if (!ShouldBlock(ctx.GetEndpoint()?.Metadata, session))
            return next(ctx);

        var body = new Dictionary<string, string>
        {
            ["error"] = AuthErrors.MsgPasswordChangeRequired,
            ["code"] = AuthErrors.PasswordChangeRequired,
        };
        return AuthJson.WriteAsync(ctx.Response, StatusCodes.Status403Forbidden, body, ctx.RequestAborted);
    }
}
