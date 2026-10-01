using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Factum.Backend.Services.Branding;

/// <summary>Logo de la organización ya validado y cargado en memoria.</summary>
public sealed record BrandingLogo(
    byte[] Data, string ContentType, string Extension, int Width, int Height, string Version);

/// <summary>Identidad de la organización normalizada (trim, límites, logo validado).</summary>
public sealed record BrandingSnapshot(
    string? OrganizationName, IReadOnlyList<string> ContactLines, BrandingLogo? Logo);

public interface IBrandingService
{
    BrandingSnapshot Current { get; }
}

/// <summary>
/// Lee la sección <c>Branding</c> UNA sola vez (al construirse; se registra como singleton y
/// <c>Program.cs</c> lo resuelve al arrancar para que los warnings salgan en el log de
/// inicio). Para cambiar el logo o el nombre hay que reiniciar el backend. Nunca tira: si
/// algo es inválido, loguea un warning y sigue sin ese dato.
/// </summary>
public sealed class BrandingService : IBrandingService
{
    public const int MaxNameLength = 150;
    public const int MaxContactLines = 6;
    public const int MaxContactLineLength = 150;
    public const long MaxLogoBytes = 1_048_576;
    public const int MinLogoSide = 16;
    public const int MaxLogoSide = 4096;

    public BrandingSnapshot Current { get; }

    public BrandingService(IOptions<BrandingOptions> options, IHostEnvironment env,
        ILogger<BrandingService> logger)
    {
        var o = options.Value ?? new BrandingOptions();
        Current = new BrandingSnapshot(
            NormalizeName(o.OrganizationName, logger),
            NormalizeContactLines(o.ContactLines, logger),
            LoadLogo(o.OrganizationLogo, env.ContentRootPath, logger));
    }

    private static string? NormalizeName(string? raw, ILogger logger)
    {
        var name = raw?.Trim() ?? "";
        if (name.Length == 0) return null;
        if (name.Length > MaxNameLength)
        {
            logger.LogWarning("Branding: OrganizationName supera {Max} caracteres; se trunca", MaxNameLength);
            name = name[..MaxNameLength].TrimEnd();
        }
        return name;
    }

    private static IReadOnlyList<string> NormalizeContactLines(List<string>? raw, ILogger logger)
    {
        var lines = (raw ?? [])
            .Select(l => l?.Trim() ?? "")
            .Where(l => l.Length > 0)
            .ToList();

        if (lines.Count > MaxContactLines)
        {
            logger.LogWarning("Branding: ContactLines tiene {Count} líneas; se usan las primeras {Max}",
                lines.Count, MaxContactLines);
            lines = lines.Take(MaxContactLines).ToList();
        }

        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Length <= MaxContactLineLength) continue;
            logger.LogWarning("Branding: ContactLines[{Index}] supera {Max} caracteres; se trunca",
                i, MaxContactLineLength);
            lines[i] = lines[i][..MaxContactLineLength].TrimEnd();
        }

        return lines.AsReadOnly();
    }

    private static BrandingLogo? LoadLogo(string? configured, string contentRoot, ILogger logger)
    {
        var raw = configured?.Trim() ?? "";
        if (raw.Length == 0) return null;

        BrandingLogo? Ignore(string reason)
        {
            logger.LogWarning("Branding: logo ignorado ({Reason})", reason);
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(Path.IsPathRooted(raw) ? raw : Path.Combine(contentRoot, raw));
            if (!File.Exists(fullPath)) return Ignore("el archivo no existe");

            var length = new FileInfo(fullPath).Length;
            if (length == 0) return Ignore("el archivo está vacío");
            if (length > MaxLogoBytes) return Ignore($"pesa {length} bytes, el máximo es {MaxLogoBytes}");

            var data = File.ReadAllBytes(fullPath);
            if (!ImageProbe.TryDetect(data, out var contentType, out var w, out var h))
                return Ignore("no es un PNG ni un JPEG válido");
            if (w < MinLogoSide || h < MinLogoSide || w > MaxLogoSide || h > MaxLogoSide)
                return Ignore($"mide {w}×{h} px, cada lado tiene que estar entre {MinLogoSide} y {MaxLogoSide}");

            var version = Convert.ToHexStringLower(SHA256.HashData(data))[..12];
            var ext = contentType == "image/png" ? ".png" : ".jpg";
            logger.LogInformation("Branding: logo de la organización cargado ({ContentType}, {W}×{H}, v={Version})",
                contentType, w, h, version);
            return new BrandingLogo(data, contentType, ext, w, h, version);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException
                                       or System.Security.SecurityException)
        {
            return Ignore($"no se pudo leer: {ex.GetType().Name}");
        }
    }
}
