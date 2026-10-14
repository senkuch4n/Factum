using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Factum.Backend.Tests.Admin;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Factum.Backend.Tests.FichaTecnica;

/// <summary>
/// Escrituras de la ficha técnica contra un Mongo real (metadatos-dispositivo §7.1, §13.3, opcional). Se saltea sin
/// <c>FACTUM_TEST_MONGO</c>. Usa una base PROPIA (<c>factum_test_&lt;guid&gt;</c>) que crea y al final borra; nunca toca
/// la base <c>factum</c> ni la de desarrollo.
/// </summary>
public sealed class MongoDeviceDetailsRepositoryTests : IAsyncLifetime
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
        Device = new DeviceInfo { Serial = DeviceDetailsTestData.Serial },
    };

    [MongoFact]
    public async Task Escrituras_respetan_Editable_y_la_ficha_existente()
    {
        var repo = Repo();
        await repo.InsertAsync(NewCase("draft", CaseStatus.Draft));
        await repo.InsertAsync(NewCase("done", CaseStatus.Completed));

        // Documento sin DeviceDetails → null; clock/options sin ficha no matchean.
        Assert.Null((await repo.FindByIdAsync("draft"))!.DeviceDetails);
        var close = new ClockMeasurement { Kind = ClockKinds.Close, RecordedAt = DateTime.UtcNow, Reason = "timeout" };
        Assert.False(await repo.SetClockCloseAsync("draft", close));
        Assert.False(await repo.SetIncludeSsidAsync("draft", true));

        var details = DeviceDetailsTestData.Android("Casa Ana");
        Assert.True(await repo.SetDeviceDetailsAsync("draft", details));
        Assert.False(await repo.SetDeviceDetailsAsync("done", details)); // Completed: no editable

        Assert.True(await repo.SetClockCloseAsync("draft", close));
        Assert.True(await repo.SetIncludeSsidAsync("draft", true));
        var read = (await repo.FindByIdAsync("draft"))!.DeviceDetails!;
        Assert.True(read.IncludeSsidInReport);
        Assert.Equal("timeout", read.ClockClose!.Reason);
        Assert.Equal(10038, read.ClockStart!.DeviceMinusPcMs);
        Assert.Equal("Casa Ana", read.Fields["wifi_ssid"].Value);
        Assert.Null(read.Fields["wifi_ssid"].Reason);
        Assert.Equal(20, read.Fields.Count);

        // Las claves de la ficha se guardan en snake_case (subdocumento Fields) y los nulos no se escriben.
        var raw = await new MongoClient(_conn).GetDatabase(_db).GetCollection<BsonDocument>("cases")
            .Find(Builders<BsonDocument>.Filter.Eq("_id", "draft")).FirstAsync();
        var fields = raw["DeviceDetails"]["Fields"].AsBsonDocument;
        Assert.True(fields.Contains("serial_number"));
        Assert.False(fields["serial_number"].AsBsonDocument.Contains("Reason"));
        Assert.False(raw["DeviceDetails"]["ClockClose"].AsBsonDocument.Contains("DeviceMinusPcMs"));

        Assert.False(await repo.SetClockCloseAsync("done", close));
        Assert.False(await repo.SetIncludeSsidAsync("done", false));
    }
}
