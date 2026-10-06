using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Cases;

namespace Factum.Backend.Tests;

/// <summary>Lógica pura del manifiesto (zip-local-informe-servidor §3.2, §5.1, §9.1). Sin E/S.</summary>
public sealed class EvidenceManifestTests
{
    private static readonly string ShaA = new('a', 64);
    private static readonly string ShaB = new('b', 64);

    private static Case Draft(string? storage = null, CaseStatus status = CaseStatus.Draft) =>
        new() { Id = Guid.NewGuid().ToString(), EvidenceStorage = storage, Status = status };

    // ── ResolveStorage: los 5 casos de §9.1 ─────────────────────────────────

    [Fact]
    public void Resolve_NewCase_PersistedAgent() =>
        Assert.Equal("agent", EvidenceManifest.ResolveStorage(Draft("agent"), _ => true));

    [Fact]
    public void Resolve_OldDraft_WithoutServerFiles_IsAgent() =>
        Assert.Equal("agent", EvidenceManifest.ResolveStorage(Draft(), _ => false));

    [Fact]
    public void Resolve_OldDraft_WithServerFiles_IsServer() =>
        Assert.Equal("server", EvidenceManifest.ResolveStorage(Draft(), _ => true));

    [Fact]
    public void Resolve_OldCompleted_IsServer_WithoutTouchingDisk()
    {
        var called = false;
        var storage = EvidenceManifest.ResolveStorage(Draft(status: CaseStatus.Completed), _ => called = true);
        Assert.Equal("server", storage);
        Assert.False(called);
    }

    [Fact]
    public void Resolve_NewCompleted_StaysAgent() =>
        Assert.Equal("agent", EvidenceManifest.ResolveStorage(Draft("agent", CaseStatus.Completed), _ => true));

    // ── ValidateItems ───────────────────────────────────────────────────────

    private static EvidenceHostDto Host(string? name = "PC-01") => new(name, "user", "2.0.0", "/tmp/x");

    private static ManifestValidation Validate(params EvidenceItemDto[] items) =>
        EvidenceManifest.ValidateItems(Host(), items);

    [Fact]
    public void Validate_Ok_NormalizesSource()
    {
        var v = Validate(new EvidenceItemDto("screenshot_1.png", 10, ShaA, "  "),
            new EvidenceItemDto("device_pull_a.txt", 0, ShaB, "/sdcard/a.txt"));
        Assert.True(v.IsValid);
        Assert.Null(v.Items[0].SourcePath);
        Assert.Equal("/sdcard/a.txt", v.Items[1].SourcePath);
    }

    [Theory]
    [InlineData("../x.png")]
    [InlineData("a/b.png")]
    [InlineData("con.txt")]
    [InlineData("a?.png")]
    [InlineData("")]
    [InlineData("evidencia_1234.zip")]
    [InlineData("informe_pericial_1234.docx")]
    public void Validate_InvalidName(string name)
    {
        var v = Validate(new EvidenceItemDto(name, 1, ShaA));
        Assert.False(v.IsValid);
        Assert.Equal(name, v.Filename);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ABCD")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Validate_InvalidSha(string? sha) =>
        Assert.False(Validate(new EvidenceItemDto("a.png", 1, sha)).IsValid);

    [Fact]
    public void Validate_NegativeOrMissingSize()
    {
        Assert.False(Validate(new EvidenceItemDto("a.png", -1, ShaA)).IsValid);
        Assert.False(Validate(new EvidenceItemDto("a.png", null, ShaA)).IsValid);
    }

    [Fact]
    public void Validate_Repeated()
    {
        var v = Validate(new EvidenceItemDto("a.png", 1, ShaA), new EvidenceItemDto("a.png", 2, ShaB));
        Assert.False(v.IsValid);
        Assert.Equal("a.png", v.Filename);
    }

    [Fact]
    public void Validate_HostAndEmpty()
    {
        Assert.False(EvidenceManifest.ValidateItems(Host(" "), [new EvidenceItemDto("a.png", 1, ShaA)]).IsValid);
        Assert.False(EvidenceManifest.ValidateItems(null, [new EvidenceItemDto("a.png", 1, ShaA)]).IsValid);
        Assert.False(EvidenceManifest.ValidateItems(Host(), []).IsValid);
    }

    // ── Upsert / MergeFileSources ───────────────────────────────────────────

    [Fact]
    public void Upsert_ReplacesByName_KeepsSource_OrdersOrdinal()
    {
        var existing = new List<EvidenceItem>
        {
            new() { Filename = "b.txt", Size = 1, Sha256 = ShaA, SourcePath = "/sdcard/b.txt" },
            new() { Filename = "a.png", Size = 1, Sha256 = ShaA },
        };
        var merged = EvidenceManifest.Upsert(existing,
            [new ValidatedEvidenceItem("b.txt", 5, ShaB, null), new ValidatedEvidenceItem("C.png", 2, ShaB, null)],
            DateTime.UtcNow);
        Assert.Equal(["C.png", "a.png", "b.txt"], merged.Select(m => m.Filename));
        var b = merged.Single(m => m.Filename == "b.txt");
        Assert.Equal(5, b.Size);
        Assert.Equal(ShaB, b.Sha256);
        Assert.Equal("/sdcard/b.txt", b.SourcePath);
    }

    [Fact]
    public void MergeFileSources_AddsOnlyNew()
    {
        var existing = new List<FileSource> { new() { Filename = "a.txt", SourcePath = "/x/a.txt" } };
        var merged = EvidenceManifest.MergeFileSources(existing,
        [
            new ValidatedEvidenceItem("a.txt", 1, ShaA, "/x/a.txt"),
            new ValidatedEvidenceItem("b.txt", 1, ShaA, "/x/b.txt"),
            new ValidatedEvidenceItem("c.png", 1, ShaA, null),
        ]);
        Assert.Equal(["a.txt", "b.txt"], merged.Select(s => s.Filename));
    }

    // ── Matches ─────────────────────────────────────────────────────────────

    private static List<EvidenceItem> Manifest() =>
    [
        new() { Filename = "a.png", Size = 10, Sha256 = ShaA },
        new() { Filename = "b.txt", Size = 20, Sha256 = ShaB },
    ];

    [Fact]
    public void Matches_Equal_IsEmpty() =>
        Assert.Empty(EvidenceManifest.Matches(
            [new ManifestFileDto("b.txt", 20, ShaB), new ManifestFileDto("a.png", 10, ShaA)], Manifest()));

    [Fact]
    public void Matches_ReportsMissingExtraAndChanged()
    {
        var mismatched = EvidenceManifest.Matches(
        [
            new ManifestFileDto("a.png", 11, ShaA), // tamaño
            new ManifestFileDto("z.txt", 1, ShaA), // sobra
        ], Manifest()); // falta b.txt
        Assert.Equal(["a.png", "b.txt", "z.txt"], mismatched);
    }

    [Fact]
    public void Matches_HashAndDuplicates()
    {
        var mismatched = EvidenceManifest.Matches(
        [
            new ManifestFileDto("a.png", 10, ShaB),
            new ManifestFileDto("b.txt", 20, ShaB),
            new ManifestFileDto("b.txt", 20, ShaB),
        ], Manifest());
        Assert.Equal(["a.png", "b.txt"], mismatched);
    }

    // ── ReportImageNames / ToFileInfos ──────────────────────────────────────

    [Fact]
    public void ReportImageNames_OnlyNonEmptyScreenshots()
    {
        var cas = Draft("agent");
        cas.Evidence =
        [
            new() { Filename = "screenshot_2.png", Size = 5, Sha256 = ShaA },
            new() { Filename = "screenshot_1.png", Size = 5, Sha256 = ShaA },
            new() { Filename = "screenshot_vacio.png", Size = 0, Sha256 = ShaA },
            new() { Filename = "captura_x.jpg", Size = 5, Sha256 = ShaA },
            new() { Filename = "grabacion.mp4", Size = 5, Sha256 = ShaA },
            new() { Filename = "foto_funcionario_screenshot.png", Size = 5, Sha256 = ShaA },
            new() { Filename = "screenshot_pull.png", Size = 5, Sha256 = ShaA, SourcePath = "/sdcard/x.png" },
        ];
        Assert.Equal(["captura_x.jpg", "screenshot_1.png", "screenshot_2.png", "screenshot_pull.png"],
            EvidenceManifest.ReportImageNames(cas));
    }

    [Fact]
    public void ToFileInfos_UsesManifest_AndFileSources()
    {
        var cas = Draft("agent");
        cas.Evidence =
        [
            new() { Filename = "b.txt", Size = 2, Sha256 = ShaB },
            new() { Filename = "a.txt", Size = 1, Sha256 = ShaA, SourcePath = "/x/a.txt" },
        ];
        cas.FileSources = [new FileSource { Filename = "b.txt", SourcePath = "/x/b.txt" }];
        var infos = EvidenceManifest.ToFileInfos(cas);
        Assert.Equal(["a.txt", "b.txt"], infos.Select(i => i.Name));
        Assert.Equal(ShaA, infos[0].Hash);
        Assert.Equal("/x/a.txt", infos[0].SourcePath);
        Assert.Equal("/x/b.txt", infos[1].SourcePath);
    }

    [Fact]
    public void SameHost_IgnoresCase() =>
        Assert.True(EvidenceManifest.SameHost("pc-perito-01", "PC-PERITO-01"));
}
