using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Factum.Backend.Tests.Admin;

/// <summary>Se saltea si no está la variable <c>FACTUM_TEST_MONGO</c> (connection string).</summary>
public sealed class MongoFactAttribute : FactAttribute
{
    public const string EnvVar = "FACTUM_TEST_MONGO";

    public MongoFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvVar)))
            Skip = $"Sin {EnvVar}: integración contra Mongo salteada.";
    }
}

/// <summary>
/// abm-clientes B35. Cada test usa una base PROPIA (<c>factum_test_&lt;guid&gt;</c>) que crea y al final
/// borra; nunca toca la base <c>factum</c> ni la de desarrollo. DNIs de prueba 99000001…
/// </summary>
public sealed class MongoAdminIntegrationTests : IAsyncLifetime
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

    [MongoFact]
    public async Task Lock_ConcurrentAcquire_OnlyOne_ThenAfterLeaseAgain()
    {
        var repo = new AdminLockRepository(Opts);
        var now = DateTime.UtcNow;
        var lease = TimeSpan.FromSeconds(10);

        var owners = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid().ToString()).ToArray();
        var results = await Task.WhenAll(owners.Select(o => Task.Run(() => repo.TryAcquireAsync("superadmin_status", o, now, lease))));
        Assert.Equal(1, results.Count(r => r));

        // Con el lease vigente nadie más entra; vencido, sí.
        Assert.False(await repo.TryAcquireAsync("superadmin_status", "otro", now.AddSeconds(5), lease));
        Assert.True(await repo.TryAcquireAsync("superadmin_status", "otro", now.AddSeconds(11), lease));

        // Release ajeno no libera; el propio sí.
        await repo.ReleaseAsync("superadmin_status", owners[0]);
        Assert.False(await repo.TryAcquireAsync("superadmin_status", "tercero", now.AddSeconds(12), lease));
        await repo.ReleaseAsync("superadmin_status", "otro");
        Assert.True(await repo.TryAcquireAsync("superadmin_status", "tercero", now.AddSeconds(12), lease));
    }

    [MongoFact]
    public async Task UpdateEditable_WithOldUpdatedAt_False_ConditionalWritesWork()
    {
        var repo = new UserRepository(Opts);
        await repo.EnsureIndexesAsync();
        var t0 = new DateTime(2026, 10, 6, 12, 0, 0, 123, DateTimeKind.Utc);
        var acc = new UserAccount
        {
            Dni = "99000001", Name = "Prueba", Role = UserRoles.Cliente, Status = UserStatuses.Activo,
            PasswordHash = "x", CreatedAt = t0, UpdatedAt = t0, PasswordChangedAt = t0,
        };
        Assert.True(await repo.TryInsertAsync(acc));

        var fields = new AccountEditableFields("Nuevo", "S", "123456", "a@b.co", "Org", "Notas");
        Assert.False(await repo.UpdateEditableFieldsIfUnchangedAsync(acc.Id, fields, t0.AddMilliseconds(-1), t0.AddSeconds(1)));
        Assert.True(await repo.UpdateEditableFieldsIfUnchangedAsync(acc.Id, fields, t0, t0.AddSeconds(1)));
        var view = (await repo.FindAdminViewByIdAsync(acc.Id))!;
        Assert.Equal("Nuevo", view.Name);
        Assert.Equal("Org", view.Organization);
        Assert.Equal(string.Empty, view.PasswordHash);

        // T3: un login fallido no cambia UpdatedAt.
        await repo.IncrementFailedLoginAsync(acc.Id, t0.AddSeconds(2));
        Assert.Equal(t0.AddSeconds(1), (await repo.FindAdminViewByIdAsync(acc.Id))!.UpdatedAt);

        Assert.True(await repo.SuspendIfActiveAsync(acc.Id, "99000002", "motivo", t0.AddSeconds(3)));
        Assert.False(await repo.SuspendIfActiveAsync(acc.Id, "99000002", null, t0.AddSeconds(3)));
        Assert.Equal("motivo", (await repo.FindAdminViewByIdAsync(acc.Id))!.SuspensionReason);
        Assert.Equal(0, await repo.CountOtherActiveSuperadminsAsync(acc.Id));
        Assert.True(await repo.ReactivateIfSuspendedAsync(acc.Id, t0.AddSeconds(4)));
        Assert.False(await repo.ReactivateIfSuspendedAsync(acc.Id, t0.AddSeconds(4)));
        var re = (await repo.FindAdminViewByIdAsync(acc.Id))!;
        Assert.Null(re.SuspensionReason);
        Assert.Null(re.SuspendedBy);

        Assert.False(await repo.UnlockIfLockedAsync(acc.Id, t0.AddSeconds(5)));
        await repo.LockAsync(acc.Id, t0.AddMinutes(15), t0.AddSeconds(5));
        Assert.True(await repo.UnlockIfLockedAsync(acc.Id, t0.AddSeconds(6)));
        Assert.Null((await repo.FindAdminViewByIdAsync(acc.Id))!.LockedUntil);
    }

    [MongoFact]
    public async Task CountByOfficerDnis_OnOwnCases()
    {
        var repo = new CaseRepository(Opts);
        foreach (var dni in new[] { "99000001", "99000001", "99000001", "99000002" })
            await repo.InsertAsync(new Case { Officer = new User { Dni = dni, Name = "Prueba" } });

        var counts = await repo.CountByOfficerDnisAsync(["99000001", "99000002", "99000003"]);

        Assert.Equal(3, counts["99000001"]);
        Assert.Equal(1, counts["99000002"]);
        Assert.False(counts.ContainsKey("99000003"));
        Assert.Empty(await repo.CountByOfficerDnisAsync([]));
    }

    [MongoFact]
    public async Task EventRepository_IndexAndPagedList()
    {
        var repo = new UserAdminEventRepository(Opts);
        await repo.EnsureIndexesAsync();
        var t0 = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 3; i++)
            await repo.InsertAsync(new UserAdminEvent { TargetUserId = "u1", At = t0.AddSeconds(i), Action = UserAdminActions.Unlock });
        await repo.InsertAsync(new UserAdminEvent { TargetUserId = "u2", At = t0, Action = UserAdminActions.Unlock });

        var page = await repo.ListByTargetAsync("u1", 0, 2);
        Assert.Equal(3, page.Count); // limit + 1
        Assert.Equal(t0.AddSeconds(2), page[0].At);

        var idx = await (await new MongoClient(_conn).GetDatabase(_db)
            .GetCollection<UserAdminEvent>(UserAdminEventRepository.CollectionName).Indexes.ListAsync()).ToListAsync();
        Assert.Contains(idx, i => i["name"] == UserAdminEventRepository.TargetAtIndexName);
    }
}
