using Factum.Backend.Services.Admin;
using Factum.Backend.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Factum.Backend.Controllers;

/// <summary>
/// Solo con <c>Auth:Mode=local</c> (abm-clientes §5, D13/T5). Fuera de local responde 404
/// <c>{ error, code: "not_available" }</c>. <see cref="Order"/> = -10: corre antes que
/// <see cref="RequireSuperadminAttribute"/>, así un cliente en dev/external también ve 404.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireLocalAuthModeAttribute : Attribute, IAuthorizationFilter, IOrderedFilter
{
    public int Order => -10;

    internal static bool IsAllowed(AuthSettings s) => s.Mode == AuthModes.Local;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var settings = context.HttpContext.RequestServices.GetRequiredService<AuthSettings>();
        if (IsAllowed(settings)) return;
        context.Result = new NotFoundObjectResult(new Dictionary<string, string>
        {
            ["error"] = AdminErrors.MsgNotAvailable,
            ["code"] = AdminErrors.NotAvailable,
        });
    }
}
