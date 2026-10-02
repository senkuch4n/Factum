using Factum.Backend.Services.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Factum.Backend.Controllers;

/// <summary>
/// Con el soporte apagado, responde <c>404 { error }</c>. Es un resource filter: corre después
/// de <c>[Authorize]</c> (sin token sigue siendo 401) y antes del model binding (un body
/// inválido no da 400).
/// </summary>
public sealed class RequireSupportEnabledFilter(SupportSettings settings) : IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        if (!settings.Enabled)
            context.Result = new NotFoundObjectResult(new { error = SupportSettings.DisabledMessage });
    }

    public void OnResourceExecuted(ResourceExecutedContext context) { }
}
