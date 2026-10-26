using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Factum.Backend.Tests.Admin;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Factum.Backend.Tests.Extractions;

/// <summary>
/// <see cref="ExtractionRepository"/> contra un Mongo real (extraccion-logica-android §6.1, §12.2). Se saltea sin
/// <c>FACTUM_TEST_MONGO</c>. Base propia <c>factum_test_&lt;guid&gt;</c> que se borra al final; nunca toca la de
/// desarrollo.
/// </summary>
public sealed class MongoExtractionRepositoryTests : IAsyncLifetime
{
    private readonly string _conn = Environment.GetEnvironmentVariable(MongoFactAttribute.EnvVar) ?? "";
    private readonly string _db = "factum_test_" + Guid.NewGuid().ToString("N");
    private static readonly DateTime Now = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

    private ExtractionRepository Repo() =>
        new(Options.Create(new MongoOptions { ConnectionString = _conn, DatabaseName = _db }));

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(_conn)) return;
        Assert.StartsWith("factum_test_", _db);
        await new MongoClient(_conn).DropDatabaseAsync(_db);
    }

    private static Extraction New(string id, string dni, string status = "running", string? caseId = null) => new()
    {
        Id = id,
        Officer = new User { Dni = dni, Name = "Perito" },
        Status = status,
        StartedAtPc = Now,
        CreatedAt = Now,
        UpdatedAt = Now,
        Device = new ExtractionDevice { Serial = "R58" },
        CaseId = caseId,
    };

    [MongoFact]
    public async Task Upsert_YLuegoFind()
    {
        var repo = Repo();
        var id = Guid.NewGuid().ToString("D");
        Assert.True(await repo.UpsertAsync(New(id, "111")));
        var found = await repo.FindByIdAsync(id);
        Assert.NotNull(found);
        Assert.Equal("111", found!.Officer.Dni);
    }

    [MongoFact]
    public async Task Upsert_OtroDueño_False()
    {
        var repo = Repo();
        var id = Guid.NewGuid().ToString("D");
        Assert.True(await repo.UpsertAsync(New(id, "111")));
        Assert.False(await repo.UpsertAsync(New(id, "222"))); // mismo _id, otro dueño
    }

    [MongoFact]
    public async Task ListByOfficer_FiltraCasoYUnlinked()
    {
        var repo = Repo();
        var linked = Guid.NewGuid().ToString("D");
        var unlinked = Guid.NewGuid().ToString("D");
        await repo.UpsertAsync(New(linked, "111", "completed", caseId: "caso-1"));
        await repo.UpsertAsync(New(unlinked, "111", "completed"));

        Assert.Equal(2, (await repo.ListByOfficerAsync("111")).Count);
        Assert.Single(await repo.ListByOfficerAsync("111", caseId: "caso-1"));
        var onlyUnlinked = await repo.ListByOfficerAsync("111", unlinkedOnly: true);
        Assert.Single(onlyUnlinked);
        Assert.Equal(unlinked, onlyUnlinked[0].Id);
    }

    [MongoFact]
    public async Task SetCase_AsociaYDesasocia_FiltraPorDueño()
    {
        var repo = Repo();
        var id = Guid.NewGuid().ToString("D");
        await repo.UpsertAsync(New(id, "111", "completed"));

        var linked = await repo.SetCaseAsync(id, "111", "caso-9", Now);
        Assert.Equal("caso-9", linked!.CaseId);

        // Dueño equivocado: no toca nada.
        Assert.Null(await repo.SetCaseAsync(id, "222", "caso-x", Now));

        var unlinked = await repo.SetCaseAsync(id, "111", null, null);
        Assert.Null(unlinked!.CaseId);
    }
}
