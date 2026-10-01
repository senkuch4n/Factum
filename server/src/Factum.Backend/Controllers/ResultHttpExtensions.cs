using Factum.Backend.Common;
using Microsoft.AspNetCore.Mvc;

namespace Factum.Backend.Controllers;

/// <summary>Mapea un <see cref="Result{T}"/> fallido a HTTP según su <see cref="ErrorKind"/>.</summary>
public static class ResultHttpExtensions
{
    public static IActionResult ErrorResult<T>(this ControllerBase c, Result<T> result)
    {
        var error = result.Error ?? "Error";
        return result.Kind switch
        {
            ErrorKind.NotFound => c.NotFound(new { error }),
            ErrorKind.Forbidden => c.Forbid(),
            ErrorKind.Conflict => c.Conflict(new { error }),
            ErrorKind.Validation when result.Missing is { Count: > 0 } missing =>
                c.BadRequest(new { error, missing }),
            _ => c.BadRequest(new { error }),
        };
    }
}
