using Factum.Backend.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Factum.Backend.Infrastructure;

/// <summary>Imagen de la marca: logo (membrete) o isotipo (cierre del informe).</summary>
public enum BrandingImageKind { Logo, Isotype }

/// <summary>Qué hacer con una imagen al guardar (marca-por-cliente §6.1, DT5).</summary>
public enum BrandingImageAction { Keep, Replace, Remove }

/// <summary><c>Replace</c> lleva la imagen nueva; <c>Keep</c> y <c>Remove</c>, null.</summary>
public sealed record ImageChange(BrandingImageAction Action, AccountBrandingImage? Image)
{
    public static readonly ImageChange Keep = new(BrandingImageAction.Keep, null);
    public static readonly ImageChange Remove = new(BrandingImageAction.Remove, null);
}

/// <summary>Valores ya normalizados y validados por <c>BrandingRules</c> que escribe un guardado.</summary>
public sealed record BrandingUpdate(string OrganizationName, List<string> ContactLines,
    string PrimaryColor, string AccentColor, ImageChange Logo, ImageChange Isotype,
    DateTime UpdatedAt, string UpdatedBy);

/// <summary>
/// Marca del informe por cuenta (colección <c>account_brandings</c>, marca-por-cliente §6.1). Todas las
/// operaciones filtran por <c>_id</c> (= DNI). No hay delete ni índices además de <c>_id</c>.
/// </summary>
public interface IAccountBrandingRepository
{
    /// <summary>Documento completo, con bytes. Lo usa el resolver del informe.</summary>
    Task<AccountBranding?> FindAsync(string dni, CancellationToken ct = default);
    /// <summary>Proyección sin <c>Logo.Data</c> ni <c>Isotype.Data</c> (GET y diff del guardado).</summary>
    Task<AccountBranding?> FindMetaAsync(string dni, CancellationToken ct = default);
    /// <summary>Solo la subimagen pedida (con bytes), para servirla. null si no hay documento o imagen.</summary>
    Task<AccountBrandingImage?> FindImageAsync(string dni, BrandingImageKind kind, CancellationToken ct = default);
    /// <summary>InsertOne. false si ya existe (DuplicateKey).</summary>
    Task<bool> TryInsertAsync(AccountBranding doc, CancellationToken ct = default);
    /// <summary>
    /// UpdateOne con filtro <c>{ _id: dni, UpdatedAt: expectedUpdatedAt }</c>. <c>$set</c> de los textos,
    /// colores, UpdatedAt y UpdatedBy; Logo/Isotype según la acción (replace → subdocumento nuevo, remove →
    /// null, keep → no se toca). false si no matcheó (otro guardado en el medio).
    /// </summary>
    Task<bool> UpdateIfUnchangedAsync(string dni, BrandingUpdate update, DateTime expectedUpdatedAt,
        CancellationToken ct = default);
}

public sealed class AccountBrandingRepository : IAccountBrandingRepository
{
    public const string CollectionName = "account_brandings";

    private static readonly ProjectionDefinition<AccountBranding> WithoutImageData =
        Builders<AccountBranding>.Projection.Exclude("Logo.Data").Exclude("Isotype.Data");

    private readonly IMongoCollection<AccountBranding> _col;

    public AccountBrandingRepository(IOptions<MongoOptions> opts)
    {
        // MongoClient no conecta hasta el primer uso; la colección la crea Mongo con el primer insert.
        var client = new MongoClient(opts.Value.ConnectionString);
        _col = client.GetDatabase(opts.Value.DatabaseName).GetCollection<AccountBranding>(CollectionName);
    }

    private static FilterDefinition<AccountBranding> ById(string dni) =>
        Builders<AccountBranding>.Filter.Eq(b => b.Id, dni);

    public async Task<AccountBranding?> FindAsync(string dni, CancellationToken ct = default) =>
        await _col.Find(ById(dni)).FirstOrDefaultAsync(ct);

    public async Task<AccountBranding?> FindMetaAsync(string dni, CancellationToken ct = default) =>
        await _col.Find(ById(dni)).Project<AccountBranding>(WithoutImageData).FirstOrDefaultAsync(ct);

    public async Task<AccountBrandingImage?> FindImageAsync(string dni, BrandingImageKind kind,
        CancellationToken ct = default)
    {
        var projection = kind == BrandingImageKind.Logo
            ? Builders<AccountBranding>.Projection.Include(b => b.Logo)
            : Builders<AccountBranding>.Projection.Include(b => b.Isotype);
        var doc = await _col.Find(ById(dni)).Project<AccountBranding>(projection).FirstOrDefaultAsync(ct);
        return kind == BrandingImageKind.Logo ? doc?.Logo : doc?.Isotype;
    }

    public async Task<bool> TryInsertAsync(AccountBranding doc, CancellationToken ct = default)
    {
        try
        {
            await _col.InsertOneAsync(doc, cancellationToken: ct);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<bool> UpdateIfUnchangedAsync(string dni, BrandingUpdate update, DateTime expectedUpdatedAt,
        CancellationToken ct = default)
    {
        var f = Builders<AccountBranding>.Filter;
        var filter = f.Eq(b => b.Id, dni) & f.Eq(b => b.UpdatedAt, expectedUpdatedAt);
        var u = Builders<AccountBranding>.Update;
        var sets = new List<UpdateDefinition<AccountBranding>>
        {
            u.Set(b => b.OrganizationName, update.OrganizationName),
            u.Set(b => b.ContactLines, update.ContactLines),
            u.Set(b => b.PrimaryColor, update.PrimaryColor),
            u.Set(b => b.AccentColor, update.AccentColor),
            u.Set(b => b.UpdatedAt, update.UpdatedAt),
            u.Set(b => b.UpdatedBy, update.UpdatedBy),
        };
        // Keep → no se toca. Replace → la imagen nueva. Remove → null.
        if (update.Logo.Action != BrandingImageAction.Keep)
            sets.Add(u.Set(b => b.Logo, NewImage(update.Logo)));
        if (update.Isotype.Action != BrandingImageAction.Keep)
            sets.Add(u.Set(b => b.Isotype, NewImage(update.Isotype)));

        var result = await _col.UpdateOneAsync(filter, u.Combine(sets), cancellationToken: ct);
        return result.MatchedCount == 1;
    }

    private static AccountBrandingImage? NewImage(ImageChange change) =>
        change.Action == BrandingImageAction.Replace ? change.Image : null;
}
