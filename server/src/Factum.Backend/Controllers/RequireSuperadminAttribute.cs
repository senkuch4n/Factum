using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Factum.Backend.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Factum.Backend.Controllers;

/// <summary>
/// Solo superadmins (usuarios-locales §6.6; lo usan los endpoints de #12). Responde 403
/// <c>{ error, code: "superadmin_required" }</c>. Va junto con <c>[Authorize]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireSuperadminAttribute : Attribute, IAuthorizationFilter
{
    internal static bool IsAllowed(AuthSession? session) => session?.Role == UserRoles.Superadmin;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (IsAllowed(context.HttpContext.Items[AuthContextKeys.Session] as AuthSession)) return;
        context.Result = new ObjectResult(new Dictionary<string, string>
        {
            ["error"] = AuthErrors.MsgSuperadminRequired,
            ["code"] = AuthErrors.SuperadminRequired,
        })
        { StatusCode = StatusCodes.Status403Forbidden };
    }
}
