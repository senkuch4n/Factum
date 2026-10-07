using Factum.Backend.DTOs;
using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Factum.Backend.Services.Branding;

namespace Factum.Backend.Tests.Branding;

/// <summary>
/// <see cref="IAccountBrandingRepository"/> en memoria (marca-por-cliente B21): diccionario por <c>_id</c> (DNI),
/// devuelve copias (como una base real) y <see cref="FindMetaAsync"/> saca los bytes de las imágenes.
/// </summary>
public sealed class InMemoryAccountBrandingRepository : IAccountBrandingRepository
{
    private readonly Dictionary<string, AccountBranding> _docs = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public int FindCalls { get; private set; }
    public int Writes { get; private set; }
    /// <summary>Simula "otra sesión creó la marca en el medio".</summary>
    public bool InsertReturnsFalse { get; set; }
    /// <summary>Si no es null, <see cref="FindAsync"/> la tira (Mongo caído).</summary>
    public Exception? FindThrows { get; set; }

    public void Seed(AccountBranding doc)
    {
        lock (_lock) _docs[doc.Id] = Clone(doc, withData: true)!;
    }

    public AccountBranding? Get(string dni)
    {
        lock (_lock) return _docs.TryGetValue(dni, out var d) ? Clone(d, withData: true) : null;
    }

    public int Count { get { lock (_lock) return _docs.Count; } }

    public Task<AccountBranding?> FindAsync(string dni, CancellationToken ct = default)
    {
        FindCalls++;
        if (FindThrows is { } ex) throw ex;
        return Task.FromResult(Get(dni));
    }

    public Task<AccountBranding?> FindMetaAsync(string dni, CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult(_docs.TryGetValue(dni, out var d) ? Clone(d, withData: false) : null);
    }

    public Task<AccountBrandingImage?> FindImageAsync(string dni, BrandingImageKind kind, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_docs.TryGetValue(dni, out var d)) return Task.FromResult<AccountBrandingImage?>(null);
            return Task.FromResult(CloneImage(kind == BrandingImageKind.Logo ? d.Logo : d.Isotype, true));
        }
    }

    public Task<bool> TryInsertAsync(AccountBranding doc, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (InsertReturnsFalse || _docs.ContainsKey(doc.Id)) return Task.FromResult(false);
            Writes++;
            _docs[doc.Id] = Clone(doc, withData: true)!;
            return Task.FromResult(true);
        }
    }

    public Task<bool> UpdateIfUnchangedAsync(string dni, BrandingUpdate update, DateTime expectedUpdatedAt,
        CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_docs.TryGetValue(dni, out var d) || d.UpdatedAt != expectedUpdatedAt) return Task.FromResult(false);
            Writes++;
            d.OrganizationName = update.OrganizationName;
            d.ContactLines = update.ContactLines.ToList();
            d.PrimaryColor = update.PrimaryColor;
            d.AccentColor = update.AccentColor;
            d.UpdatedAt = update.UpdatedAt;
            d.UpdatedBy = update.UpdatedBy;
            if (update.Logo.Action != BrandingImageAction.Keep)
                d.Logo = update.Logo.Action == BrandingImageAction.Replace ? CloneImage(update.Logo.Image, true) : null;
            if (update.Isotype.Action != BrandingImageAction.Keep)
                d.Isotype = update.Isotype.Action == BrandingImageAction.Replace ? CloneImage(update.Isotype.Image, true) : null;
            return Task.FromResult(true);
        }
    }

    private static AccountBranding? Clone(AccountBranding? d, bool withData) => d is null ? null : new AccountBranding
    {
        Id = d.Id,
        OrganizationName = d.OrganizationName,
        ContactLines = d.ContactLines.ToList(),
        Logo = CloneImage(d.Logo, withData),
        Isotype = CloneImage(d.Isotype, withData),
        PrimaryColor = d.PrimaryColor,
        AccentColor = d.AccentColor,
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt,
        UpdatedBy = d.UpdatedBy,
    };

    private static AccountBrandingImage? CloneImage(AccountBrandingImage? i, bool withData) => i is null ? null : new AccountBrandingImage
    {
        Data = withData ? i.Data.ToArray() : [],
        ContentType = i.ContentType,
        Width = i.Width,
        Height = i.Height,
        Size = i.Size,
        Version = i.Version,
    };
}

/// <summary>Instalación fija.</summary>
public sealed class FixedInstallationBranding(BrandingSnapshot current) : IBrandingService
{
    public BrandingSnapshot Current { get; } = current;
}

internal static class BrandingTestData
{
    public const string DniA = "99000001";
    public const string DniB = "99000002";
    public const string DniC = "99000003";

    /// <summary>Metadata completa con defaults "nada cambia" (todo vacío, imágenes keep).</summary>
    public static BrandingSaveMetadata Meta(
        string? name = "", List<string?>? lines = null, string? primary = "", string? accent = "",
        string? logoAction = "keep", string? isotypeAction = "keep", DateTime? expected = null) =>
        new(name, lines ?? [], primary, accent, logoAction, isotypeAction, expected);

    public static BrandingSaveInput Input(BrandingSaveMetadata meta, byte[]? logo = null, byte[]? isotype = null) =>
        new(meta, logo, isotype);

    /// <summary>Imagen ya normalizada (como la arma BrandingRules) a partir de un PNG.</summary>
    public static AccountBrandingImage Image(byte[] png)
    {
        var (img, err) = BrandingRules.ValidateImage("logo", png);
        Assert.Null(err);
        return img!;
    }
}
