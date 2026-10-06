using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Factum.Backend.Tests.Admin;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using static Factum.Backend.Tests.Branding.BrandingTestData;

namespace Factum.Backend.Tests.Branding;

/// <summary>
/// marca-por-cliente B25. Cada test usa una base PROPIA (<c>factum_test_&lt;guid&gt;</c>) que crea y al final
/// borra; nunca toca la base <c>factum</c> ni la de desarrollo. Se saltea sin <c>FACTUM_TEST_MONGO</c>.
/// </summary>
public sealed class MongoBrandingIntegrationTests : IAsyncLifetime
{
    private readonly string _conn = Environment.GetEnvironmentVariable(MongoFactAttribute.EnvVar) ?? "";
    private readonly string _db = "factum_test_" + Guid.NewGuid().ToString("N");
    private IOptions<MongoOptions> Opts => Options.Create(new MongoOptions { ConnectionString = _conn, DatabaseName = _db });

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(_conn)) return;
        Assert.StartsWith("factum_test_", _db);
        await new MongoClient(_conn).DropDatabaseAsync(_db);
    }

    private static readonly DateTime T0 = new(2026, 10, 6, 12, 0, 0, 123, DateTimeKind.Utc);

    private static AccountBranding Doc() => new()
    {
        Id = DniA,
        OrganizationName = "Estudio Ficticio A",
        ContactLines = ["L1", "L2"],
        PrimaryColor = "1F3A93",
        AccentColor = "E6ECFA",
        Logo = Image(TestImages.Png(64, 32)),
        Isotype = Image(TestImages.Png(32, 32)),
        CreatedAt = T0,
        UpdatedAt = T0,
        UpdatedBy = DniA,
    };

    [MongoFact]
    public async Task Insert_FindMetaSinData_FindConData_DuplicadoFalse()
    {
        var repo = new AccountBrandingRepository(Opts);
        var doc = Doc();
        Assert.True(await repo.TryInsertAsync(doc));
        Assert.False(await repo.TryInsertAsync(Doc()));

        var meta = (await repo.FindMetaAsync(DniA))!;
        Assert.Equal("Estudio Ficticio A", meta.OrganizationName);
        Assert.Equal(["L1", "L2"], meta.ContactLines);
        Assert.Empty(meta.Logo!.Data);
        Assert.Empty(meta.Isotype!.Data);
        Assert.Equal(doc.Logo!.Version, meta.Logo.Version);
        Assert.Equal((64, 32), (meta.Logo.Width, meta.Logo.Height));
        Assert.Equal(T0, meta.UpdatedAt);
        Assert.Equal(DateTimeKind.Utc, meta.UpdatedAt.Kind);

        var full = (await repo.FindAsync(DniA))!;
        Assert.Equal(doc.Logo.Data, full.Logo!.Data);
        Assert.Equal(doc.Isotype!.Data, full.Isotype!.Data);

        Assert.Null(await repo.FindAsync(DniB));
        Assert.Null(await repo.FindMetaAsync(DniB));
    }

    [MongoFact]
    public async Task UpdateIfUnchanged_ViejoFalse_CorrectoTrue_KeepConservaRemoveDejaNull()
    {
        var repo = new AccountBrandingRepository(Opts);
        var doc = Doc();
        Assert.True(await repo.TryInsertAsync(doc));
        var t1 = T0.AddSeconds(5);
        var update = new BrandingUpdate("Nuevo", ["X"], "", "E6ECFA", ImageChange.Keep, ImageChange.Remove, t1, DniB);

        Assert.False(await repo.UpdateIfUnchangedAsync(DniA, update, T0.AddMilliseconds(-1)));
        Assert.True(await repo.UpdateIfUnchangedAsync(DniA, update, T0));
        Assert.False(await repo.UpdateIfUnchangedAsync(DniA, update, T0)); // ya cambió UpdatedAt

        var after = (await repo.FindAsync(DniA))!;
        Assert.Equal("Nuevo", after.OrganizationName);
        Assert.Equal(["X"], after.ContactLines);
        Assert.Equal("", after.PrimaryColor);
        Assert.Equal(t1, after.UpdatedAt);
        Assert.Equal(DniB, after.UpdatedBy);
        Assert.Equal(T0, after.CreatedAt);
        Assert.Equal(doc.Logo!.Data, after.Logo!.Data); // keep
        Assert.Null(after.Isotype);                      // remove

        var newLogo = Image(TestImages.Png(20, 20));
        var replace = update with { Logo = new ImageChange(BrandingImageAction.Replace, newLogo), UpdatedAt = t1.AddSeconds(1) };
        Assert.True(await repo.UpdateIfUnchangedAsync(DniA, replace, t1));
        Assert.Equal(newLogo.Data, (await repo.FindAsync(DniA))!.Logo!.Data);
    }

    [MongoFact]
    public async Task FindImage_SoloLaPedida()
    {
        var repo = new AccountBrandingRepository(Opts);
        var doc = Doc();
        Assert.True(await repo.TryInsertAsync(doc));

        var logo = (await repo.FindImageAsync(DniA, BrandingImageKind.Logo))!;
        Assert.Equal(doc.Logo!.Data, logo.Data);
        Assert.Equal("image/png", logo.ContentType);
        var iso = (await repo.FindImageAsync(DniA, BrandingImageKind.Isotype))!;
        Assert.Equal(doc.Isotype!.Data, iso.Data);
        Assert.Null(await repo.FindImageAsync(DniB, BrandingImageKind.Logo));

        // La proyección trae solo el subdocumento pedido.
        var raw = await new MongoClient(_conn).GetDatabase(_db)
            .GetCollection<MongoDB.Bson.BsonDocument>(AccountBrandingRepository.CollectionName)
            .Find(Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("_id", DniA))
            .Project(Builders<MongoDB.Bson.BsonDocument>.Projection.Include("Logo"))
            .FirstAsync();
        Assert.Equal(["_id", "Logo"], raw.Names);
    }
}
