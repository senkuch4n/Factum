using Factum.Backend.Common;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Backend.Controllers;

/// <summary>Mapea un <see cref="Result{T}"/> fallido a HTTP según su <see cref="ErrorKind"/>.</summary>
public static class ResultHttpExtensions
{
    public static IActionResult ErrorResult<T>(this ControllerBase c, Result<T> result)
    {
        var error = result.Error ?? "Error";
        if (result.Details is not null) return DetailedErrorResult(c, result, error);
        return result.Kind switch
        {
            ErrorKind.NotFound => c.NotFound(new { error }),
            ErrorKind.Forbidden => c.Forbid(),
            ErrorKind.Conflict => c.Conflict(new { error }),
            ErrorKind.Validation when result.Missing is { Count: > 0 } missing =>
                LogValidation(c, error, missing).BadRequest(new { error, missing }),
            ErrorKind.PayloadTooLarge => c.StatusCode(StatusCodes.Status413PayloadTooLarge, new { error }),
            ErrorKind.InsufficientStorage => c.StatusCode(StatusCodes.Status507InsufficientStorage, new { error }),
            ErrorKind.ServerError => c.StatusCode(StatusCodes.Status500InternalServerError, new { error }),
            _ => LogValidation(c, error, result.Missing).BadRequest(new { error }),
        };
    }

    // subida-archivos-grandes §5.4: { error[, missing], ...Details }. Las claves de un diccionario
    // no pasan por la naming policy, así que Details ya viene en snake_case literal.
    private static IActionResult DetailedErrorResult<T>(ControllerBase c, Result<T> result, string error)
    {
        if (result.Kind == ErrorKind.Forbidden) return c.Forbid();

        var body = new Dictionary<string, object?> { ["error"] = error };
        if (result.Missing is { Count: > 0 } missing) body["missing"] = missing;
        foreach (var (key, value) in result.Details!) body[key] = value;

        var status = result.Kind switch
        {
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            ErrorKind.PayloadTooLarge => StatusCodes.Status413PayloadTooLarge,
            ErrorKind.InsufficientStorage => StatusCodes.Status507InsufficientStorage,
            ErrorKind.ServerError => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status400BadRequest, // Validation / Failure
        };
        if (status == StatusCodes.Status400BadRequest) LogValidation(c, error, result.Missing);
        return c.StatusCode(status, body);
    }

    // Loguea en Warning cada 400 de validación, para poder diagnosticar (ver
    // progress/incidente-2026-10-08-imei-crear-caso.md: hoy un 400 no dejaba rastro en el log).
    // Solo registramos método, ruta, mensaje de error (genérico) y las CLAVES faltantes — nunca
    // valores de los campos ni el body, que pueden traer datos del caso / PII.
    private static ControllerBase LogValidation(ControllerBase c, string error, IReadOnlyList<string>? missing)
    {
        var logger = c.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Factum.Backend.Validacion");
        var request = c.HttpContext.Request;
        logger.LogWarning(
            "Validación 400 en {Method} {Path}: {Error}. Faltantes: {Missing}",
            request.Method, request.Path, error,
            missing is { Count: > 0 } ? string.Join(", ", missing) : "ninguno");
        return c;
    }
}
