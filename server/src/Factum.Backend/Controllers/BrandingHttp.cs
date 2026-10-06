using System.Text.Json;
using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Admin;
using Factum.Backend.Services.Branding;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Factum.Backend.Controllers;

/// <summary>
/// Helpers HTTP compartidos por <see cref="BrandingController"/> y <see cref="AdminBrandingController"/>
/// (marca-por-cliente §6.5): mapeo de errores, lectura del PUT multipart (§6.5.1) y servido de imágenes (§6.5.2).
/// </summary>
internal static class BrandingHttp
{
    public const string PartMetadata = "metadata";
    public const string PartLogo = "logo";
    public const string PartIsotype = "isotype";

    /// <summary>§6.5: <c>code</c> → HTTP.</summary>
    internal static int StatusFor(string? code) => code switch
    {
        BrandingErrors.ValidationFailed => StatusCodes.Status400BadRequest,
        BrandingErrors.UserNotFound or BrandingErrors.NotAvailable or BrandingErrors.ImageNotFound
            => StatusCodes.Status404NotFound,
        BrandingErrors.StaleUpdate => StatusCodes.Status409Conflict,
        BrandingErrors.RequestTooLarge => StatusCodes.Status413PayloadTooLarge,
        _ => StatusCodes.Status500InternalServerError,
    };

    /// <summary><c>{ error, code[, field][, index][, contrast][, min_contrast][, max_bytes] }</c>: claves literales.</summary>
    internal static IActionResult Error<T>(Result<T> result)
    {
        var body = new Dictionary<string, object?> { ["error"] = result.Error };
        if (result.Details is { } details)
            foreach (var (k, v) in details) body[k] = v;
        return new ObjectResult(body) { StatusCode = StatusFor(AdminErrors.CodeOf(result)) };
    }

    internal static IActionResult Respond<T>(Result<T> result, Func<T, IActionResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value!) : Error(result);

    internal static BrandingActor ActorOf(HttpContext ctx)
    {
        var user = UserOf(ctx);
        return new BrandingActor(user.Dni, user.Name, ctx.Connection.RemoteIpAddress?.ToString());
    }

    internal static User UserOf(HttpContext ctx) => (User)ctx.Items[Infrastructure.AuthContextKeys.User]!;

    private static Result<BrandingSaveInput> InvalidRequest() =>
        AdminErrors.Fail<BrandingSaveInput>(BrandingErrors.ValidationFailed, BrandingErrors.MsgInvalidRequest,
            BrandingErrors.FieldMetadata);

    internal static Result<BrandingSaveInput> TooLarge() =>
        AdminErrors.Fail<BrandingSaveInput>(BrandingErrors.RequestTooLarge, BrandingErrors.MsgRequestTooLarge, null,
            new Dictionary<string, object?> { [BrandingErrors.MaxBytesKey] = BrandingLimits.MaxRequestBytes });

    /// <summary>
    /// §6.5.1: fija el tope del cuerpo ANTES de leer, rechaza por <c>Content-Length</c>, exige
    /// <c>multipart/form-data</c>, lee la parte <c>metadata</c> (JSON snake_case con las opciones de MVC) y los
    /// archivos <c>logo</c>/<c>isotype</c> (un archivo de más de 1 MiB no se copia a memoria). Cualquier otra parte
    /// se ignora.
    /// </summary>
    internal static async Task<Result<BrandingSaveInput>> ReadAsync(HttpContext ctx, JsonSerializerOptions json,
        CancellationToken ct)
    {
        var request = ctx.Request;
        var bodySize = ctx.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySize is { IsReadOnly: false }) bodySize.MaxRequestBodySize = BrandingLimits.MaxRequestBytes;
        if (request.ContentLength is { } length && length > BrandingLimits.MaxRequestBytes) return TooLarge();

        if (!request.HasFormContentType ||
            !MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType) ||
            !mediaType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            return InvalidRequest();

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(new Microsoft.AspNetCore.Http.Features.FormOptions
            {
                MultipartBodyLengthLimit = BrandingLimits.MaxRequestBytes,
            }, ct);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return TooLarge();
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("limit", StringComparison.OrdinalIgnoreCase))
        {
            // "Multipart body length limit … exceeded" (y los demás topes del lector de formularios).
            return TooLarge();
        }
        catch (InvalidDataException)
        {
            // Cuerpo multipart roto o vacío: no es un problema de tamaño.
            return InvalidRequest();
        }
        catch (BadHttpRequestException)
        {
            return InvalidRequest();
        }

        if (!form.TryGetValue(PartMetadata, out var rawMetadata) || rawMetadata.Count != 1 ||
            string.IsNullOrWhiteSpace(rawMetadata[0]))
            return InvalidRequest();

        BrandingSaveMetadata? metadata;
        try
        {
            metadata = JsonSerializer.Deserialize<BrandingSaveMetadata>(rawMetadata[0]!, json);
        }
        catch (JsonException)
        {
            return InvalidRequest();
        }
        if (metadata is null) return InvalidRequest();

        var (logo, logoTooBig) = await ReadFileAsync(form.Files.GetFile(PartLogo), ct);
        var (isotype, isotypeTooBig) = await ReadFileAsync(form.Files.GetFile(PartIsotype), ct);
        return Result.Ok(new BrandingSaveInput(metadata, logo, isotype, logoTooBig, isotypeTooBig));
    }

    private static async Task<(byte[]? Data, bool TooBig)> ReadFileAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null) return (null, false);
        if (file.Length > BrandingService.MaxLogoBytes) return (null, true);
        using var ms = new MemoryStream((int)file.Length);
        await file.CopyToAsync(ms, ct);
        return (ms.ToArray(), false);
    }

    /// <summary>
    /// §6.5.2: bytes con <c>nosniff</c>, CSP <c>sandbox</c>, <c>ETag</c> = versión y
    /// <c>Cache-Control: private, max-age=86400</c>; 304 si <c>If-None-Match</c> coincide (también <c>W/</c> o
    /// <c>*</c>). El <c>?v=</c> de la query es solo cache-busting del client y se ignora (DT4).
    /// </summary>
    internal static IActionResult ImageResult(HttpRequest request, HttpResponse response, AccountBrandingImage img)
    {
        var etag = $"\"{img.Version}\"";
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        response.Headers.ETag = etag;
        response.Headers.CacheControl = "private, max-age=86400";

        var ifNoneMatch = request.Headers.IfNoneMatch.ToString();
        if (ifNoneMatch.Length > 0 &&
            ifNoneMatch.Split(',').Select(t => t.Trim()).Any(t => t == etag || t == "W/" + etag || t == "*"))
            return new StatusCodeResult(StatusCodes.Status304NotModified);

        return new FileContentResult(img.Data, img.ContentType);
    }
}
