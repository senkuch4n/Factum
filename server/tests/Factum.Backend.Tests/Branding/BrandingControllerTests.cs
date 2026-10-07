using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Factum.Backend.Controllers;
using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Admin;
using Factum.Backend.Services.Branding;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using static Factum.Backend.Tests.Branding.BrandingTestData;

namespace Factum.Backend.Tests.Branding;

/// <summary>marca-por-cliente B24: contrato §8.1 (claves snake_case), códigos → HTTP, multipart y headers de imagen. Sin Mongo.</summary>
public sealed class BrandingControllerTests
{
    private static readonly JsonSerializerOptions Snake = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static string[] Keys(JsonElement e) => e.EnumerateObject().Select(p => p.Name).ToArray();

    private static readonly string[] BrandingKeys =
    [
        "exists", "organization_name", "contact_lines", "primary_color", "accent_color", "logo", "isotype",
        "updated_at", "updated_by", "suggested_organization_name",
    ];

    // ── DTOs ─────────────────────────────────────────────────────────────────

    [Fact]
    public void AccountBrandingDto_ClavesDelContrato()
    {
        var img = new BrandingImageDto("/api/branding/logo?v=abcdefabcdef", "image/png", 64, 32, 1234, "abcdefabcdef");
        var dto = new AccountBrandingDto(true, "Estudio", ["L1"], "#1F3A93", null, img, null,
            new DateTime(2026, 10, 6, 12, 0, 0, 123, DateTimeKind.Utc), DniA, null);

        var root = JsonDocument.Parse(JsonSerializer.Serialize(dto, Snake)).RootElement;
        Assert.Equal(BrandingKeys, Keys(root));
        Assert.Equal(["url", "content_type", "width", "height", "size", "version"], Keys(root.GetProperty("logo")));
        Assert.Equal("2026-10-06T12:00:00.123Z", root.GetProperty("updated_at").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("isotype").ValueKind);

        Assert.Equal(["branding"],
            Keys(JsonDocument.Parse(JsonSerializer.Serialize(new AccountBrandingResponse(dto), Snake)).RootElement));
        Assert.Equal(["branding", "changed"],
            Keys(JsonDocument.Parse(JsonSerializer.Serialize(new AccountBrandingSaveResponse(dto, true), Snake)).RootElement));
    }

    [Fact]
    public void Dto_SinMarca_VaciosYNull()
    {
        var dto = AccountBrandingService.ToDto(null, "/api/branding", "Sugerida");
        var root = JsonDocument.Parse(JsonSerializer.Serialize(dto, Snake)).RootElement;
        Assert.False(root.GetProperty("exists").GetBoolean());
        Assert.Equal("", root.GetProperty("organization_name").GetString());
        Assert.Equal(0, root.GetProperty("contact_lines").GetArrayLength());
        foreach (var k in new[] { "primary_color", "accent_color", "logo", "isotype", "updated_at", "updated_by" })
            Assert.Equal(JsonValueKind.Null, root.GetProperty(k).ValueKind);
        Assert.Equal("Sugerida", root.GetProperty("suggested_organization_name").GetString());
    }

    [Fact]
    public void Metadata_SeLeeDesdeSnakeCase()
    {
        var m = JsonSerializer.Deserialize<BrandingSaveMetadata>(
            """{"organization_name":"E","contact_lines":["a",null],"primary_color":"#1F3A93","accent_color":"","logo_action":"replace","isotype_action":"keep","expected_updated_at":"2026-10-06T12:00:00.123Z"}""",
            Snake)!;
        Assert.Equal("E", m.OrganizationName);
        Assert.Equal(new List<string?> { "a", null }, m.ContactLines);
        Assert.Equal(("replace", "keep"), (m.LogoAction, m.IsotypeAction));
        Assert.Equal(new DateTime(2026, 10, 6, 12, 0, 0, 123, DateTimeKind.Utc), m.ExpectedUpdatedAt);
        Assert.Null(JsonSerializer.Deserialize<BrandingSaveMetadata>(
            """{"organization_name":"E","expected_updated_at":null}""", Snake)!.ExpectedUpdatedAt);
    }

    // ── códigos → HTTP ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("validation_failed", 400)]
    [InlineData("user_not_found", 404)]
    [InlineData("not_available", 404)]
    [InlineData("image_not_found", 404)]
    [InlineData("stale_update", 409)]
    [InlineData("request_too_large", 413)]
    [InlineData("otro", 500)]
    public void StatusFor_CadaCodigo(string code, int status) => Assert.Equal(status, BrandingHttp.StatusFor(code));

    [Fact]
    public void KindFor_CodigosNuevos()
    {
        Assert.Equal(ErrorKind.NotFound, AdminErrors.KindFor("image_not_found"));
        Assert.Equal(ErrorKind.PayloadTooLarge, AdminErrors.KindFor("request_too_large"));
    }

    [Fact]
    public void Error_CuerpoConClavesLiterales()
    {
        var r = AdminErrors.Fail<int>("validation_failed", "msg", "primary_color",
            new Dictionary<string, object?> { ["contrast"] = 1.0, ["min_contrast"] = 4.5 });
        var result = Assert.IsType<ObjectResult>(BrandingHttp.Error(r));
        Assert.Equal(400, result.StatusCode);
        var body = Assert.IsType<Dictionary<string, object?>>(result.Value);
        Assert.Equal(["error", "code", "field", "contrast", "min_contrast"], body.Keys);
    }

    // ── imágenes ─────────────────────────────────────────────────────────────

    private static AccountBrandingImage Img() => Image(TestImages.Png(20, 20));

    [Fact]
    public void Imagen_Headers_YBytes()
    {
        var ctx = new DefaultHttpContext();
        var img = Img();
        var result = BrandingHttp.ImageResult(ctx.Request, ctx.Response, img);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("image/png", file.ContentType);
        Assert.Equal(img.Data, file.FileContents);
        Assert.Equal($"\"{img.Version}\"", ctx.Response.Headers.ETag.ToString());
        Assert.Equal("private, max-age=86400", ctx.Response.Headers.CacheControl.ToString());
        Assert.Equal("nosniff", ctx.Response.Headers.XContentTypeOptions.ToString());
        Assert.Equal("default-src 'none'; sandbox", ctx.Response.Headers.ContentSecurityPolicy.ToString());
    }

    [Theory]
    [InlineData("\"{v}\"")]
    [InlineData("W/\"{v}\"")]
    [InlineData("\"otra\", \"{v}\"")]
    [InlineData("*")]
    public void Imagen_IfNoneMatch_304(string header)
    {
        var img = Img();
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.IfNoneMatch = header.Replace("{v}", img.Version);
        var result = Assert.IsType<StatusCodeResult>(BrandingHttp.ImageResult(ctx.Request, ctx.Response, img));
        Assert.Equal(304, result.StatusCode);
        Assert.Equal($"\"{img.Version}\"", ctx.Response.Headers.ETag.ToString());
    }

    [Fact]
    public void Imagen_IfNoneMatchDistinto_200()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.IfNoneMatch = "\"000000000000\"";
        Assert.IsType<FileContentResult>(BrandingHttp.ImageResult(ctx.Request, ctx.Response, Img()));
    }

    // ── multipart (§6.5.1) ───────────────────────────────────────────────────

    private static async Task<DefaultHttpContext> MultipartContext(string? metadata, (string Name, byte[] Data)[] files)
    {
        using var content = new MultipartFormDataContent("frontera-de-prueba");
        if (metadata is not null) content.Add(new StringContent(metadata, Encoding.UTF8), "metadata");
        foreach (var (name, data) in files)
        {
            var part = new ByteArrayContent(data);
            part.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(part, name, name + ".png");
        }
        var bytes = await content.ReadAsByteArrayAsync();
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "PUT";
        ctx.Request.ContentType = content.Headers.ContentType!.ToString();
        ctx.Request.ContentLength = bytes.Length;
        ctx.Request.Body = new MemoryStream(bytes);
        return ctx;
    }

    private const string ValidMetadata =
        """{"organization_name":"Estudio","contact_lines":["L1"],"primary_color":"","accent_color":"","logo_action":"replace","isotype_action":"keep","expected_updated_at":null}""";

    [Fact]
    public async Task Multipart_Valido_LeeMetadataYArchivos_IgnoraOtrasPartes()
    {
        var png = TestImages.Png(32, 32);
        var ctx = await MultipartContext(ValidMetadata, [("logo", png), ("otra", [1, 2, 3])]);

        var r = await BrandingHttp.ReadAsync(ctx, Snake, CancellationToken.None);

        Assert.True(r.IsSuccess, r.Error);
        Assert.Equal("Estudio", r.Value!.Metadata.OrganizationName);
        Assert.Equal("replace", r.Value.Metadata.LogoAction);
        Assert.Equal(png, r.Value.LogoBytes);
        Assert.Null(r.Value.IsotypeBytes);
        Assert.False(r.Value.LogoTooBig);
    }

    [Fact]
    public async Task Multipart_ArchivoDeMasDe1MiB_TooBigSinLeer()
    {
        var ctx = await MultipartContext(ValidMetadata, [("logo", new byte[BrandingServiceMax + 10])]);
        var r = await BrandingHttp.ReadAsync(ctx, Snake, CancellationToken.None);
        Assert.True(r.IsSuccess, r.Error);
        Assert.True(r.Value!.LogoTooBig);
        Assert.Null(r.Value.LogoBytes);
    }

    private const long BrandingServiceMax = BrandingService.MaxLogoBytes;

    [Theory]
    [InlineData(null)]
    [InlineData("no es json")]
    [InlineData("null")]
    public async Task Multipart_SinMetadataOInvalida_InvalidRequest(string? metadata)
    {
        // Con una parte de más, así el cuerpo multipart es válido y lo que falla es la metadata.
        var ctx = await MultipartContext(metadata, [("otra", [1, 2, 3])]);
        var r = await BrandingHttp.ReadAsync(ctx, Snake, CancellationToken.None);
        Assert.False(r.IsSuccess);
        Assert.Equal("validation_failed", AdminErrors.CodeOf(r));
        Assert.Equal("metadata", r.Details!["field"]);
        Assert.Equal("La solicitud no es válida. Recargá e intentá de nuevo.", r.Error);
    }

    [Fact]
    public async Task MultipartVacio_InvalidRequest_No413()
    {
        var ctx = await MultipartContext(null, []);
        var r = await BrandingHttp.ReadAsync(ctx, Snake, CancellationToken.None);
        Assert.Equal("validation_failed", AdminErrors.CodeOf(r));
        Assert.Equal("metadata", r.Details!["field"]);
    }

    [Fact]
    public async Task NoMultipart_InvalidRequest()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.ContentType = "application/json";
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(ValidMetadata));
        var r = await BrandingHttp.ReadAsync(ctx, Snake, CancellationToken.None);
        Assert.Equal("metadata", r.Details!["field"]);
    }

    [Fact]
    public async Task ContentLengthMayorAlTope_413ConMaxBytes()
    {
        var ctx = await MultipartContext(ValidMetadata, []);
        ctx.Request.ContentLength = BrandingLimits.MaxRequestBytes + 1;
        var r = await BrandingHttp.ReadAsync(ctx, Snake, CancellationToken.None);
        Assert.Equal("request_too_large", AdminErrors.CodeOf(r));
        Assert.Equal(BrandingLimits.MaxRequestBytes, r.Details!["max_bytes"]);
        Assert.Equal("La marca supera el tamaño máximo permitido (2,5 MB en total).", r.Error);
        Assert.Equal(413, BrandingHttp.StatusFor(AdminErrors.CodeOf(r)));
    }

    [Fact]
    public async Task CuerpoMultipartMayorAlTopeSinContentLength_413()
    {
        var ctx = await MultipartContext(ValidMetadata, [("logo", new byte[BrandingLimits.MaxRequestBytes + 100])]);
        ctx.Request.ContentLength = null;
        var r = await BrandingHttp.ReadAsync(ctx, Snake, CancellationToken.None);
        Assert.Equal("request_too_large", AdminErrors.CodeOf(r));
    }

    // ── filtros del panel ────────────────────────────────────────────────────

    [Fact]
    public void Panel_FiltrosEnElMismoOrdenQueAdminUsers()
    {
        static string[] Filters(Type t) => t.GetCustomAttributes(true)
            .Where(a => a is RequireLocalAuthModeAttribute or RequireSuperadminAttribute or Microsoft.AspNetCore.Authorization.AuthorizeAttribute)
            .Select(a => a.GetType().Name).OrderBy(n => n).ToArray();
        Assert.Equal(Filters(typeof(AdminUsersController)), Filters(typeof(AdminBrandingController)));
        Assert.Equal(-10, new RequireLocalAuthModeAttribute().Order);
        Assert.Contains(typeof(BrandingController).GetCustomAttributes(true),
            a => a is Microsoft.AspNetCore.Authorization.AuthorizeAttribute);
        Assert.DoesNotContain(typeof(BrandingController).GetCustomAttributes(true),
            a => a is RequireSuperadminAttribute or RequireLocalAuthModeAttribute);
    }
}
