namespace Factum.Backend.Infrastructure;

/// <summary>
/// api-hardening §4.2 (API-01): headers de seguridad de una API JSON en todas las respuestas, aplicados
/// en <c>Response.OnStarting</c> (así también salen en los errores que escribe <see cref="ApiErrorMiddleware"/>
/// después de <c>Response.Clear()</c>). Cada header se pone solo si la respuesta no lo trae ya (la vista
/// previa de capturas fija su propia CSP). Además saca <c>Server</c>.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public const string ApiContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var ctx = (HttpContext)state;
            var headers = ctx.Response.Headers;
            SetIfMissing(headers, "X-Content-Type-Options", "nosniff");
            SetIfMissing(headers, "X-Frame-Options", "DENY");
            SetIfMissing(headers, "Referrer-Policy", "no-referrer");
            // Swagger UI (solo Development) necesita scripts y estilos.
            if (!ctx.Request.Path.StartsWithSegments("/swagger"))
                SetIfMissing(headers, "Content-Security-Policy", ApiContentSecurityPolicy);
            headers.Remove("Server");
            return Task.CompletedTask;
        }, context);
        return next(context);
    }

    private static void SetIfMissing(IHeaderDictionary headers, string name, string value)
    {
        if (!headers.ContainsKey(name)) headers[name] = value;
    }
}
