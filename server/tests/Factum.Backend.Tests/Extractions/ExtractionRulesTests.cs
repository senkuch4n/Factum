using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Extractions;

namespace Factum.Backend.Tests.Extractions;

public class ExtractionRulesTests
{
    private static readonly string Id = Guid.NewGuid().ToString("D");
    private static readonly User Officer = new() { Dni = "30111222", Name = "Ana Pérez" };
    private static readonly DateTime Now = new(2026, 10, 25, 18, 0, 0, DateTimeKind.Utc);
    private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static UpsertExtractionRequest Running() => new(
        Status: "running",
        Device: new ExtractionDeviceDto(Serial: "R58", Manufacturer: "samsung", Model: "SM-M127F"),
        StartedAtPc: new DateTimeOffset(Now));

    private static UpsertExtractionRequest Completed() => Running() with
    {
        Status = "completed",
        FinishedAtPc = new DateTimeOffset(Now.AddMinutes(10)),
        ManifestSha256 = Sha,
    };

    [Fact]
    public void IdNoGuid_Invalido()
    {
        var r = ExtractionRules.Apply(null, "no-es-guid", Running(), Officer, Now);
        Assert.Equal(ExtractionRuleError.Invalid, r.Error);
    }

    [Fact]
    public void Crear_Running_Ok_ConOfficer()
    {
        var r = ExtractionRules.Apply(null, Id, Running(), Officer, Now);
        Assert.Equal(ExtractionRuleError.None, r.Error);
        Assert.Equal("30111222", r.Value!.Officer.Dni);
        Assert.Equal(ExtractionStatuses.Running, r.Value.Status);
        Assert.False(r.BecameFinalFirstTime);
    }

    [Fact]
    public void TerminalSinHash_Invalido()
    {
        var req = Running() with { Status = "completed", FinishedAtPc = new DateTimeOffset(Now) };
        var r = ExtractionRules.Apply(null, Id, req, Officer, Now);
        Assert.Equal(ExtractionRuleError.Invalid, r.Error);
    }

    [Fact]
    public void TerminalSinFinishedAt_Invalido()
    {
        var req = Running() with { Status = "completed", ManifestSha256 = Sha };
        var r = ExtractionRules.Apply(null, Id, req, Officer, Now);
        Assert.Equal(ExtractionRuleError.Invalid, r.Error);
    }

    [Fact]
    public void InterruptedSinInterruption_Invalido()
    {
        var req = Running() with
        {
            Status = "interrupted",
            FinishedAtPc = new DateTimeOffset(Now),
            ManifestSha256 = Sha,
        };
        var r = ExtractionRules.Apply(null, Id, req, Officer, Now);
        Assert.Equal(ExtractionRuleError.Invalid, r.Error);
    }

    [Fact]
    public void Completar_FijaFinalRegistradoYManifiesto()
    {
        var existing = ExtractionRules.Apply(null, Id, Running(), Officer, Now).Value!;
        var r = ExtractionRules.Apply(existing, Id, Completed(), Officer, Now.AddMinutes(11));
        Assert.Equal(ExtractionRuleError.None, r.Error);
        Assert.True(r.ManifestJustSet);
        Assert.True(r.BecameFinalFirstTime);
        Assert.Equal(Sha, r.Value!.ManifestSha256);
    }

    [Fact]
    public void ManifestSha256_InmutableSiCambia()
    {
        var existing = ExtractionRules.Apply(null, Id, Completed(), Officer, Now).Value!;
        var otro = Completed() with { ManifestSha256 = new string('a', 64) };
        var r = ExtractionRules.Apply(existing, Id, otro, Officer, Now.AddMinutes(1));
        Assert.Equal(ExtractionRuleError.Immutable, r.Error);
    }

    [Fact]
    public void MismoTerminal_Idempotente()
    {
        var existing = ExtractionRules.Apply(null, Id, Completed(), Officer, Now).Value!;
        var r = ExtractionRules.Apply(existing, Id, Completed(), Officer, Now.AddMinutes(1));
        Assert.Equal(ExtractionRuleError.None, r.Error);
        Assert.False(r.ManifestJustSet);          // el hash ya estaba
        Assert.False(r.BecameFinalFirstTime);     // ya era terminal
    }

    [Fact]
    public void TerminalAOtroEstado_Finished()
    {
        var existing = ExtractionRules.Apply(null, Id, Completed(), Officer, Now).Value!;
        var r = ExtractionRules.Apply(existing, Id, Running(), Officer, Now.AddMinutes(1));
        Assert.Equal(ExtractionRuleError.Finished, r.Error);
    }

    [Fact]
    public void SerialInmutable()
    {
        var existing = ExtractionRules.Apply(null, Id, Running(), Officer, Now).Value!;
        var otro = Running() with { Device = new ExtractionDeviceDto(Serial: "OTRO") };
        var r = ExtractionRules.Apply(existing, Id, otro, Officer, Now.AddMinutes(1));
        Assert.Equal(ExtractionRuleError.Immutable, r.Error);
    }

    [Fact]
    public void StartedAtPcInmutable()
    {
        var existing = ExtractionRules.Apply(null, Id, Running(), Officer, Now).Value!;
        var otro = Running() with { StartedAtPc = new DateTimeOffset(Now.AddHours(1)) };
        var r = ExtractionRules.Apply(existing, Id, otro, Officer, Now.AddMinutes(1));
        Assert.Equal(ExtractionRuleError.Immutable, r.Error);
    }

    [Fact]
    public void CategoriaDesconocida_Invalida()
    {
        var req = Running() with { Categories = [new ExtractionCategoryDto(Key: "inexistente", Status: "done")] };
        var r = ExtractionRules.Apply(null, Id, req, Officer, Now);
        Assert.Equal(ExtractionRuleError.Invalid, r.Error);
    }

    [Fact]
    public void CategoriaDuplicada_Invalida()
    {
        var req = Running() with
        {
            Categories =
            [
                new ExtractionCategoryDto(Key: "contacts", Status: "done"),
                new ExtractionCategoryDto(Key: "contacts", Status: "done"),
            ],
        };
        var r = ExtractionRules.Apply(null, Id, req, Officer, Now);
        Assert.Equal(ExtractionRuleError.Invalid, r.Error);
    }

    [Fact]
    public void NumeroNegativo_Invalido()
    {
        var req = Running() with { Totals = new ExtractionTotalsDto(Files: -1) };
        var r = ExtractionRules.Apply(null, Id, req, Officer, Now);
        Assert.Equal(ExtractionRuleError.Invalid, r.Error);
    }

    [Fact]
    public void ModificacionDesconocida_Invalida()
    {
        var req = Running() with
        {
            DeviceModifications = [new DeviceModificationDto(Kind: "formatear_disco", At: new DateTimeOffset(Now))],
        };
        var r = ExtractionRules.Apply(null, Id, req, Officer, Now);
        Assert.Equal(ExtractionRuleError.Invalid, r.Error);
    }

    [Fact]
    public void Modificaciones_AppendOnly_MergePorKindYAt()
    {
        var at1 = new DateTimeOffset(Now);
        var at2 = new DateTimeOffset(Now.AddMinutes(5));
        var first = Running() with
        {
            DeviceModifications = [new DeviceModificationDto(Kind: "connector_installed", At: at1)],
        };
        var existing = ExtractionRules.Apply(null, Id, first, Officer, Now).Value!;

        // Segundo PUT: repite la 1ª (mismo kind+at) y agrega una nueva.
        var second = Running() with
        {
            DeviceModifications =
            [
                new DeviceModificationDto(Kind: "connector_installed", At: at1),
                new DeviceModificationDto(Kind: "connector_uninstalled", At: at2),
            ],
        };
        var r = ExtractionRules.Apply(existing, Id, second, Officer, Now.AddMinutes(6));
        Assert.Equal(ExtractionRuleError.None, r.Error);
        Assert.Equal(2, r.Value!.DeviceModifications.Count); // no se duplicó la primera
    }

    [Fact]
    public void Modificaciones_TopeVeinte()
    {
        var mods = Enumerable.Range(0, 21)
            .Select(i => new DeviceModificationDto(Kind: "stay_awake_set", At: new DateTimeOffset(Now.AddSeconds(i))))
            .ToList();
        var req = Running() with { DeviceModifications = mods };
        var r = ExtractionRules.Apply(null, Id, req, Officer, Now);
        Assert.Equal(ExtractionRuleError.Invalid, r.Error);
    }

    [Fact]
    public void TextosSaneadosYRecortados()
    {
        var req = Running() with { Hostname = new string('x', 300) };
        var r = ExtractionRules.Apply(null, Id, req, Officer, Now);
        Assert.Equal(ExtractionRuleError.None, r.Error);
        Assert.Equal(100, r.Value!.Hostname.Length);
    }
}
