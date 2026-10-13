using Factum.Backend.Models;
using Factum.Backend.Services.Cases;

namespace Factum.Backend.Tests;

/// <summary>Registro idempotente del manifiesto (wizard-concurrencia-pestanas DT10, B3). Sin E/S.</summary>
public sealed class EvidenceRegistrationPlanTests
{
    private static readonly string H1 = new('1', 64);
    private static readonly string H2 = new('2', 64);
    private static readonly DateTime Before = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    private static EvidenceItem Existing(string name, string sha, long size = 10, string? source = null) =>
        new() { Filename = name, Sha256 = sha, Size = size, SourcePath = source, RegisteredAt = Before };

    private static ValidatedEvidenceItem In(string name, string sha, long size = 10, string? source = null) =>
        new(name, size, sha, source);

    [Fact]
    public void Nuevo_va_a_Added_con_now()
    {
        var p = EvidenceManifest.PlanRegistration([], [In("x.png", H1)], Now);
        Assert.True(p.Changed);
        Assert.Equal("x.png", Assert.Single(p.Added).Filename);
        Assert.Empty(p.Updated);
        Assert.Equal(Now, Assert.Single(p.Merged).RegisteredAt);
    }

    [Fact]
    public void Mismo_sha_no_cambia_y_conserva_RegisteredAt_y_Size()
    {
        var p = EvidenceManifest.PlanRegistration([Existing("x.png", H1, size: 10)], [In("x.png", H1, size: 999)], Now);
        Assert.False(p.Changed);
        Assert.Empty(p.Added);
        Assert.Empty(p.Updated);
        var item = Assert.Single(p.Merged);
        Assert.Equal(Before, item.RegisteredAt);
        Assert.Equal(10, item.Size);
    }

    [Fact]
    public void Sha_distinto_va_a_Updated_con_el_anterior()
    {
        var p = EvidenceManifest.PlanRegistration([Existing("x.png", H1, source: "/sdcard/x.png")],
            [In("x.png", H2, size: 20)], Now);
        Assert.True(p.Changed);
        Assert.Empty(p.Added);
        var (item, previous) = Assert.Single(p.Updated);
        Assert.Equal(("x.png", H2), (item.Filename, item.Sha256));
        Assert.Equal(H1, previous);
        var merged = Assert.Single(p.Merged);
        Assert.Equal((H2, 20L, Now), (merged.Sha256, merged.Size, merged.RegisteredAt));
        Assert.Equal("/sdcard/x.png", merged.SourcePath); // sin ruta nueva se conserva la anterior
    }

    [Fact]
    public void Completa_SourcePath_sin_evento()
    {
        var p = EvidenceManifest.PlanRegistration([Existing("x.png", H1)], [In("x.png", H1, source: "/sdcard/x.png")], Now);
        Assert.True(p.Changed);
        Assert.Empty(p.Added);
        Assert.Empty(p.Updated);
        var item = Assert.Single(p.Merged);
        Assert.Equal("/sdcard/x.png", item.SourcePath);
        Assert.Equal(Before, item.RegisteredAt);
    }

    [Fact]
    public void Mismo_sha_con_ruta_ya_guardada_no_cambia()
    {
        var p = EvidenceManifest.PlanRegistration([Existing("x.png", H1, source: "/a")], [In("x.png", H1, source: "/b")], Now);
        Assert.False(p.Changed);
        Assert.Equal("/a", Assert.Single(p.Merged).SourcePath);
    }

    [Fact]
    public void Lote_mixto_y_orden_ordinal()
    {
        var p = EvidenceManifest.PlanRegistration(
            [Existing("b.png", H1), Existing("c.png", H1), Existing("Z.png", H1)],
            [In("c.png", H2), In("a.png", H1), In("b.png", H1)], Now);
        Assert.True(p.Changed);
        Assert.Equal("a.png", Assert.Single(p.Added).Filename);
        Assert.Equal("c.png", Assert.Single(p.Updated).Item.Filename);
        Assert.Equal(["Z.png", "a.png", "b.png", "c.png"], p.Merged.Select(e => e.Filename));
    }

    [Fact]
    public void Lo_existente_que_no_viene_se_conserva()
    {
        var p = EvidenceManifest.PlanRegistration([Existing("viejo.png", H1)], [In("nuevo.png", H2)], Now);
        Assert.Equal(["nuevo.png", "viejo.png"], p.Merged.Select(e => e.Filename));
        Assert.Equal(Before, p.Merged.Single(e => e.Filename == "viejo.png").RegisteredAt);
    }
}
