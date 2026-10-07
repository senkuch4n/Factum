using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Factum.Backend.Controllers;

/// <summary>
/// Saca los value providers de formulario del action (patrón de ASP.NET para cuerpos en streaming).
/// Sin esto, un cuerpo con <c>Content-Type: application/x-www-form-urlencoded</c> o
/// <c>multipart/form-data</c> lo lee MVC como formulario antes del action y el action recibe el
/// stream ya consumido. subida-archivos-grandes §4.2: el <c>Content-Type</c> de la subida se ignora.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class DisableFormValueModelBindingAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var factories = context.ValueProviderFactories;
        factories.RemoveType<FormValueProviderFactory>();
        factories.RemoveType<FormFileValueProviderFactory>();
        factories.RemoveType<JQueryFormValueProviderFactory>();
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
