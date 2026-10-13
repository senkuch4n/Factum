namespace Factum.Backend.Infrastructure;

/// <summary>
/// api-hardening §4.3 (API-02, D5 A, D6 A): toda excepción no controlada sale como <c>500</c> JSON
/// <c>{ error, code: "internal_error", trace_id }</c> y los <see cref="BadHttpRequestException"/> de Kestrel
/// (body demasiado grande, timeout, etc.) con su status y el mismo formato. Nunca un stack trace, en ningún
/// entorno. Va DENTRO de CORS y de <see cref="SecurityHeadersMiddleware"/>: los dos aplican sus headers en
/// <c>OnStarting</c>, que sobrevive a <c>Response.Clear()</c>.
/// <para>Nunca se loguea el body, la query ni los headers (pueden traer <c>?token=</c> o PII): solo el Path.</para>
/// </summary>
public sealed class ApiErrorMiddleware(RequestDelegate next, ILoggerFactory loggerFactory)
{
    public const string LogCategory = "Factum.Backend.Errores";

    public const string MsgInternal =
        "Ocurrió un error interno en el servidor. Intentá de nuevo; si se repite, comunicate con soporte e indicá el código de error.";
    public const string MsgPayloadTooLarge = "La solicitud supera el tamaño máximo permitido.";
    public const string MsgRequestTimeout = "La solicitud tardó demasiado en llegar al servidor.";
    public const string MsgBadRequest = "La solicitud no es válida.";

    private readonly ILogger _log = loggerFactory.CreateLogger(LogCategory);

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
        {
            // El cliente se fue: no hay a quién responderle.
            _log.LogDebug("Request cancelada por el cliente {TraceId} en {Method} {Path}",
                ctx.TraceIdentifier, ctx.Request.Method, ctx.Request.Path.Value);
        }
        catch (BadHttpRequestException ex)
        {
            if (ctx.Response.HasStarted)
            {
                _log.LogWarning("Request rechazada {Status} {TraceId} en {Method} {Path} con la respuesta ya empezada: {Motivo}",
                    ex.StatusCode, ctx.TraceIdentifier, ctx.Request.Method, ctx.Request.Path.Value, ex.Message);
                throw;
            }
            _log.LogWarning("Request rechazada {Status} {TraceId} en {Method} {Path}: {Motivo}",
                ex.StatusCode, ctx.TraceIdentifier, ctx.Request.Method, ctx.Request.Path.Value, ex.Message);
            var (message, code) = ex.StatusCode switch
            {
                StatusCodes.Status413PayloadTooLarge => (MsgPayloadTooLarge, "payload_too_large"),
                StatusCodes.Status408RequestTimeout => (MsgRequestTimeout, "request_timeout"),
                _ => (MsgBadRequest, "bad_request"),
            };
            await WriteErrorAsync(ctx, ex.StatusCode, message, code);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error no controlado {TraceId} en {Method} {Path}",
                ctx.TraceIdentifier, ctx.Request.Method, ctx.Request.Path.Value);
            if (ctx.Response.HasStarted) throw; // no se puede cambiar: Kestrel corta la conexión, como antes
            await WriteErrorAsync(ctx, StatusCodes.Status500InternalServerError, MsgInternal, "internal_error");
        }
    }

    internal static async Task WriteErrorAsync(HttpContext ctx, int status, string message, string code)
    {
        // Clear borra headers y status ya puestos, pero NO los callbacks OnStarting (CORS y headers de seguridad).
        ctx.Response.Clear();
        ctx.Response.StatusCode = status;
        ctx.Response.Headers.CacheControl = "no-store";
        var body = new Dictionary<string, object?>
        {
            ["error"] = message,
            ["code"] = code,
            ["trace_id"] = ctx.TraceIdentifier,
        };
        // Content-Type por defecto: application/json; charset=utf-8.
        await ctx.Response.WriteAsJsonAsync(body, CancellationToken.None);
    }
}
