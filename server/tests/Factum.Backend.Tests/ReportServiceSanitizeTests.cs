using System.Runtime.CompilerServices;
using System.Text;
using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Reports;
using Microsoft.Extensions.Logging.Abstractions;
using static Factum.Backend.Tests.ReportTestSupport;

namespace Factum.Backend.Tests;

/// <summary>
/// wizard-pulido-ux §9.4: nombre del ZIP y del DOCX sin cortar palabras y conservando tildes
/// (Sanitize DT12), nombres ya registrados (ResolveZipFilename DT10) y DOCX con la misma base que el
/// ZIP (StemFromZip DT11). Sin Mongo; el único test con disco usa un directorio temporal propio.
/// </summary>
public sealed class ReportServiceSanitizeTests : IDisposable
{
    // Misma cadena que server/src/Factum.Agent/Common/AgentFileNames.cs (ZipNameRegex).
    private const string TatanaZipRegex = @"^evidencia_[\p{L}\p{N}_]{1,60}\.zip$";

    private const string Shot = "screenshot_20261001_101530.png";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "factum-sanitize-" + Guid.NewGuid().ToString("N"));

    public ReportServiceSanitizeTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static readonly string Word55 = new('a', 55);
    private static readonly string Word60 = new('b', 60);

    public static TheoryData<string> AllInputs() => new()
    {
        "CCC 12345/2026_María José Fernández de la Santísima Trinidad",
        "CCC 12.345/2026 – Señoría_María José Fernández de la Santísima Trinidad Gómez",
        new string('x', 80),
        Word55 + "_" + new string('c', 10),
        Word60 + "_" + "masletras",
        "/// ---",
        "",
        "María",
        "Ana😀Paz",
    };

    private static Case CaseWith(string nro, string titular)
    {
        var cas = MakeCase();
        cas.NroReferencia = nro;
        cas.NombreDenunciante = titular;
        return cas;
    }

    // 1. Corte en palabra
    [Fact]
    public void Sanitize_CutsOnWordBoundary_AndKeepsAccents()
    {
        var r = ReportService.Sanitize("CCC 12345/2026_María José Fernández de la Santísima Trinidad");
        Assert.True(r.Length <= 60);
        Assert.False(r.EndsWith('_'));
        Assert.Contains("María", r);
        Assert.Contains("Fernández", r);

        var longer = ReportService.Sanitize("CCC 12.345/2026 – Señoría_María José Fernández de la Santísima Trinidad Gómez");
        Assert.True(longer.Length <= 60);
        Assert.False(longer.EndsWith('_'));
        Assert.Contains("Señoría", longer);
        // Termina en una palabra completa del original.
        var lastWord = longer[(longer.LastIndexOf('_') + 1)..];
        Assert.Contains(lastWord, new[] { "CCC", "12", "345", "2026", "Señoría", "María", "José", "Fernández", "de", "la", "Santísima", "Trinidad", "Gómez" });
    }

    // 2. Sin separador
    [Fact]
    public void Sanitize_NoSeparator_HardCutAt60() =>
        Assert.Equal(60, ReportService.Sanitize(new string('x', 80)).Length);

    // 3. Corte exacto en el borde
    [Fact]
    public void Sanitize_BoundaryCases()
    {
        Assert.Equal(Word55, ReportService.Sanitize(Word55 + "_" + new string('c', 10)));
        Assert.Equal(Word60, ReportService.Sanitize(Word60 + "_masletras"));
    }

    // 4. Vacío
    [Theory]
    [InlineData("/// ---")]
    [InlineData("")]
    public void Sanitize_Empty_IsCaso(string input) => Assert.Equal("caso", ReportService.Sanitize(input));

    // 5. NFD → NFC
    [Fact]
    public void Sanitize_NormalizesToNfc()
    {
        var r = ReportService.Sanitize("María");
        Assert.Equal("María", r);
        Assert.DoesNotContain('_', r);
        Assert.True(r.IsNormalized(NormalizationForm.FormC));
    }

    // 6. Surrogates
    [Fact]
    public void Sanitize_Surrogates_BecomeUnderscores()
    {
        var r = ReportService.Sanitize("Ana😀Paz");
        Assert.Equal("Ana__Paz", r);
        Assert.DoesNotContain(r, char.IsSurrogate);
    }

    [Fact]
    public void Sanitize_DoesNotCollapseRepeatedUnderscores() =>
        Assert.Equal("a___b", ReportService.Sanitize("a - b"));

    // 7. Compatibilidad con Tatana
    [Theory]
    [MemberData(nameof(AllInputs))]
    public void ZipFilenameFor_MatchesTatanaRegex(string titular)
    {
        var zip = ReportService.ZipFilenameFor(CaseWith("", titular));
        Assert.Matches(TatanaZipRegex, zip);
        Assert.Matches(ReportService.ZipNameRegex, zip);
        Assert.True(zip.IsNormalized(NormalizationForm.FormC));
    }

    [Fact]
    public void ZipNameRegex_IsSameStringAsTatana()
    {
        Assert.Equal(TatanaZipRegex, ReportService.ZipNameRegex.ToString());
        var agentFile = FindRepoFile(Path.Combine("server", "src", "Factum.Agent", "Common", "AgentFileNames.cs"));
        Assert.Contains("@\"" + TatanaZipRegex + "\"", File.ReadAllText(agentFile));
    }

    // Sube desde la carpeta de ESTE archivo fuente (no desde bin/, que puede estar en otro lado).
    private static string FindRepoFile(string relative, [CallerFilePath] string thisFile = "")
    {
        for (var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile)!); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("No se encontró " + relative + " subiendo desde " + thisFile);
    }

    // 8. ResolveZipFilename
    // Datos donde la regla vieja (corte duro en 60) y la nueva (corte en palabra) difieren.
    private const string LongNro = "CCC 12345/2026";
    private const string LongTitular = "María José Fernández de la Santísima Trinidades Gómez";

    [Fact]
    public void Resolve_RegisteredZipFilename_IsKept()
    {
        var cas = CaseWith(LongNro, LongTitular);
        cas.ZipFilename = "evidencia_1234_titular.zip";
        Assert.NotEqual(cas.ZipFilename, ReportService.ZipFilenameFor(cas));
        Assert.Equal("evidencia_1234_titular.zip", ReportService.ResolveZipFilename(cas));
    }

    [Fact]
    public void Resolve_PendingLegacyNameOfSameData_IsKept()
    {
        var cas = CaseWith(LongNro, LongTitular);
        var legacy = ReportService.LegacyZipFilenameFor(cas);
        Assert.NotEqual(legacy, ReportService.ZipFilenameFor(cas));
        Assert.Matches(TatanaZipRegex, legacy);
        cas.PendingGeneration = new PendingGeneration { Id = "x", ZipFilename = legacy };
        Assert.Equal(legacy, ReportService.ResolveZipFilename(cas));
    }

    [Fact]
    public void Resolve_PendingWithOldData_UsesNewRule()
    {
        var old = CaseWith("9999/2025", LongTitular);
        var cas = CaseWith(LongNro, LongTitular);
        cas.PendingGeneration = new PendingGeneration { Id = "x", ZipFilename = ReportService.LegacyZipFilenameFor(old) };
        Assert.Equal(ReportService.ZipFilenameFor(cas), ReportService.ResolveZipFilename(cas));
    }

    [Fact]
    public void Resolve_InvalidRegisteredName_UsesNewRule()
    {
        var cas = CaseWith(LongNro, LongTitular);
        cas.ZipFilename = "x.zip";
        Assert.Equal(ReportService.ZipFilenameFor(cas), ReportService.ResolveZipFilename(cas));
    }

    [Fact]
    public void Resolve_Nothing_IsZipFilenameFor()
    {
        var cas = CaseWith(LongNro, LongTitular);
        Assert.Equal(ReportService.ZipFilenameFor(cas), ReportService.ResolveZipFilename(cas));
    }

    // 9. StemFromZip
    [Fact]
    public void StemFromZip_ExtractsMiddle_OrFallsBackToSanitize()
    {
        var cas = CaseWith(LongNro, LongTitular);
        Assert.Equal("abc_def", ReportService.StemFromZip("evidencia_abc_def.zip", cas));
        Assert.Equal(ReportService.Sanitize($"{LongNro}_{LongTitular}"), ReportService.StemFromZip("otro_nombre.zip", cas));
        Assert.Equal(ReportService.Sanitize($"{LongNro}_{LongTitular}"), ReportService.StemFromZip("evidencia_.zip", cas));
    }

    // 10. DOCX = ZIP
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GenerateReport_DocxUsesSameStemAsZip(bool legacy)
    {
        var images = Path.Combine(_root, "images-" + legacy);
        var output = Path.Combine(_root, "output-" + legacy);
        Directory.CreateDirectory(images);
        Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(images, Shot), TestImages.Png(40, 80));

        var cas = CaseWith(LongNro, LongTitular);
        cas.EvidenceStorage = EvidenceStorages.Agent;
        cas.CaptureRoles = [new CaptureRole { Filename = Shot, Role = CaptureRole.ImeiModelo }];
        cas.ReportTexts = new ReportTexts
        {
            OperacionesRealizadas = "Operaciones ficticias.",
            AseguramientoEvidencia = "Aseguramiento ficticio.",
            Resultados = "Resultados ficticios.",
            ValoracionTecnica = "Valoración ficticia.",
            Conclusiones = "Conclusiones ficticias.",
            Formato = ReportTextFormats.Markdown,
        };
        var zipName = legacy ? ReportService.LegacyZipFilenameFor(cas) : ReportService.ZipFilenameFor(cas);
        if (legacy) Assert.NotEqual(zipName, ReportService.ZipFilenameFor(cas));

        // Resto de un intento anterior con la base "de los datos": se limpia aunque la base del ZIP sea otra.
        var dataStem = ReportService.Sanitize($"{cas.NroReferencia}_{cas.NombreDenunciante}");
        var staleOther = Path.Combine(output, $"informe_forense_{dataStem}.pdf");
        File.WriteAllText(staleOther, "resto");

        var sha = new string('1', 64);
        var service = new ReportService(new FakeBranding(FactumBrand()), new FakeSettings(),
            NullLogger<ReportService>.Instance, V6);
        var result = await service.GenerateReportAsync(cas, [new FileInfoDto(Shot, 100, sha, DateTime.UtcNow)],
            new Dictionary<string, string> { [Shot] = sha }, images, output, zipName, new string('f', 64));

        var stem = zipName["evidencia_".Length..^".zip".Length];
        Assert.Equal($"informe_pericial_{stem}.docx", result.PdfFilename);
        Assert.False(File.Exists(staleOther));
    }
}
