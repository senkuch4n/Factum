using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Factum.Backend.Services.Cases;
using Factum.Backend.Tests.Admin;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Factum.Backend.Tests.SesionGrabacion;

/// <summary>
/// Escrituras de la grabación de la sesión contra un Mongo real (grabacion-sesion-completa §6.3, opcional). Se saltea sin
/// <c>FACTUM_TEST_MONGO</c>. Usa una base PROPIA (<c>factum_test_&lt;guid&gt;</c>) que crea y al final borra; nunca toca
/// la base <c>factum</c> ni la de desarrollo.
/// </summary>
public sealed class MongoRecordingSessionRepositoryTests : IAsyncLifetime
{
    private readonly string _conn = Environment.GetEnvironmentVariable(MongoFactAttribute.EnvVar) ?? "";
    private readonly string _db = "factum_test_" + Guid.NewGuid().ToString("N");

    private CaseRepository Repo() =>
        new(Options.Create(new MongoOptions { ConnectionString = _conn, DatabaseName = _db }));

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(_conn)) return;
        Assert.StartsWith("factum_test_", _db);
        await new MongoClient(_conn).DropDatabaseAsync(_db);
    }

    private static Case NewCase(string id, CaseStatus status) => new()
    {
        Id = id,
        Status = status,
        Officer = new User { Dni = "99000001", Name = "Perito Ficticio" },
    };

    [MongoFact]
    public async Task Crear_Switch_YSyncConConcurrencia()
    {
        var repo = Repo();
        await repo.InsertAsync(NewCase("draft", CaseStatus.Draft));
        await repo.InsertAsync(NewCase("done", CaseStatus.Completed));

        // Sin el campo: null, y el switch sin sub-documento no matchea.
        Assert.Null((await repo.FindByIdAsync("draft"))!.RecordingSession);
        Assert.False(await repo.SetRecordingSessionSettingsAsync("draft", true, false, "optional_off", DateTime.UtcNow));

        var created = new Models.RecordingSession { Enabled = true, ModeAtChange = "optional_off", SettingsUpdatedAt = DateTime.UtcNow, SegmentMinutes = 30 };
        Assert.True(await repo.CreateRecordingSessionAsync("draft", created));
        Assert.False(await repo.CreateRecordingSessionAsync("draft", created)); // ya existe
        Assert.False(await repo.CreateRecordingSessionAsync("done", created)); // no editable

        Assert.True(await repo.SetRecordingSessionSettingsAsync("draft", false, true, "required", DateTime.UtcNow));
        var rs = (await repo.FindByIdAsync("draft"))!.RecordingSession!;
        Assert.False(rs.Enabled);
        Assert.True(rs.WithCamera);
        Assert.Equal("required", rs.ModeAtChange);
        Assert.Empty(rs.Segments);
        Assert.Null(rs.SyncedAt);

        // Primer sync: SyncedAt esperado null.
        var merged = RecordingSessionRules.Merge(rs, SessionData.Valid(SessionData.Snapshot(state: "stopped"))).Merged!;
        merged.SyncedAt = DateTime.UtcNow;
        Assert.True(await repo.SetRecordingSessionAsync("draft", null, merged));
        var after = (await repo.FindByIdAsync("draft"))!.RecordingSession!;
        Assert.Single(after.Segments);
        Assert.Equal(SessionData.SessionId, after.SessionId);
        Assert.Equal("stopped", after.State);
        Assert.False(after.Enabled); // el sync no toca el switch
        Assert.True(after.WithCamera);
        Assert.NotNull(after.SyncedAt);

        // Con el SyncedAt viejo (null) ya no matchea; con el leído, sí.
        Assert.False(await repo.SetRecordingSessionAsync("draft", null, merged));
        merged.SyncedAt = DateTime.UtcNow.AddSeconds(1);
        merged.Camera = null;
        Assert.True(await repo.SetRecordingSessionAsync("draft", after.SyncedAt, merged));

        // El listado no trae el diario.
        var listed = await repo.ListByOfficerAsync("99000001");
        Assert.All(listed, c => Assert.Null(c.RecordingSession));

        // En el documento: campos PascalCase, sin nulls.
        var raw = await new MongoClient(_conn).GetDatabase(_db).GetCollection<BsonDocument>("cases")
            .Find(new BsonDocument("_id", "draft")).FirstAsync();
        var sub = raw["RecordingSession"].AsBsonDocument;
        Assert.True(sub.Contains("Segments"));
        Assert.False(sub.Contains("Camera"));
        Assert.Equal(BsonType.DateTime, sub["SyncedAt"].BsonType);
    }
}
