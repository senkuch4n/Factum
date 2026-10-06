using DocumentFormat.OpenXml.Packaging;
using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Reports;
using Microsoft.Extensions.Logging.Abstractions;
using static Factum.Backend.Tests.ReportTestSupport;

namespace Factum.Backend.Tests;

/// <summary>
/// <c>ReportService.GenerateReportAsync</c> (zip-local-informe-servidor §5.7): solo el DOCX, con
/// las capturas en una carpeta distinta de la de salida y la tabla de hashes del manifiesto.
/// Carpetas temporales propias y datos ficticios.
/// </summary>
public sealed class ReportServiceAgentTests : IDisposable
{
    private const string Shot = "screenshot_20261001_101530.png";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "factum-agentreport-" + Guid.NewGuid().ToString("N"));

    public ReportServiceAgentTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static ReportService Service() =>
        new(new FakeBranding(FactumBrand()), new FakeSettings(), NullLogger<ReportService>.Instance, V6);

    private static Case AgentCase()
    {
        var cas = MakeCase();
        cas.EvidenceStorage = EvidenceStorages.Agent;
        cas.CaptureRoles = [new CaptureRole { Filename = Shot, Role = CaptureRole.ImeiModelo }];
        cas.ReportTexts = new ReportTexts
        {
            OperacionesRealizadas = "Operaciones ficticias.",
            AseguramientoEvidencia = "Aseguramiento ficticio.",
            Resultados = "Texto:\n\n![](captura:" + Shot + ")",
            ValoracionTecnica = "Valoración ficticia.",
            Conclusiones = "Conclusiones ficticias.",
            Formato = ReportTextFormats.Markdown,
        };
        return cas;
    }

    [Fact]
    public async Task Docx_UsesImagesDir_ManifestHashes_AndLeavesOnlyDocx()
    {
        var images = Path.Combine(_root, "images");
        var output = Path.Combine(_root, "output");
        Directory.CreateDirectory(images);
        Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(images, Shot), TestImages.Png(40, 80));

        // Hashes del manifiesto: valores que NO son los de ningún archivo en disco (el video y el
        // .txt ni siquiera están): la tabla tiene que salir de acá.
        var shaShot = new string('1', 64);
        var shaVideo = new string('2', 64);
        var shaTxt = new string('3', 64);
        var zipHash = new string('f', 64);
        var stamp = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
        var evidence = new List<FileInfoDto>
        {
            new("grabacion_1.mkv", 5_000_000, shaVideo, stamp),
            new(Shot, 100, shaShot, stamp),
            new("device_pull_chat.txt", 10, shaTxt, stamp, "/sdcard/chat.txt"),
        };
        var hashes = new Dictionary<string, string>
        {
            [Shot] = shaShot, ["grabacion_1.mkv"] = shaVideo, ["device_pull_chat.txt"] = shaTxt,
        };

        var cas = AgentCase();
        var zipName = ReportService.ZipFilenameFor(cas);
        var result = await Service().GenerateReportAsync(cas, evidence, hashes, images, output, zipName, zipHash);

        Assert.Equal([Path.GetFileName(result.PdfPath)], Directory.GetFiles(output).Select(Path.GetFileName));
        Assert.StartsWith("informe_pericial_", result.PdfFilename);
        Assert.EndsWith(".docx", result.PdfFilename);
        Assert.Matches("^[0-9a-f]{64}$", result.ReportHash);
        Assert.Equal([Shot], Directory.GetFiles(images).Select(Path.GetFileName)); // no toca las imágenes

        using var doc = WordprocessingDocument.Open(result.PdfPath, false);
        var text = TextOf(doc.MainDocumentPart!.Document.Body!);
        Assert.Contains(shaShot, text);
        Assert.Contains(shaVideo, text);
        Assert.Contains(shaTxt, text);
        Assert.Contains(zipHash, text);
        Assert.Contains(zipName, text);
        Assert.Contains("Origen: /sdcard/chat.txt", text);
        // La captura (rol imei + cuerpo) quedó embebida desde imagesDir.
        Assert.NotEmpty(doc.MainDocumentPart.ImageParts);
    }

    [Fact]
    public async Task Docx_DoesNotTouchExistingZip_AndCleansStaleDocx()
    {
        var images = Path.Combine(_root, "images2");
        var output = Path.Combine(_root, "output2");
        Directory.CreateDirectory(images);
        Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(images, Shot), TestImages.Png(40, 80));
        var cas = AgentCase();
        var zipName = ReportService.ZipFilenameFor(cas);
        File.WriteAllText(Path.Combine(output, zipName), "zip viejo");
        var safe = ReportService.Sanitize($"{cas.NroReferencia}_{cas.NombreDenunciante}");
        File.WriteAllText(Path.Combine(output, $"informe_forense_{safe}.pdf"), "resto");

        var stamp = DateTime.UtcNow;
        var sha = new string('1', 64);
        await Service().GenerateReportAsync(cas, [new FileInfoDto(Shot, 100, sha, stamp)],
            new Dictionary<string, string> { [Shot] = sha }, images, output, zipName, new string('f', 64));

        Assert.Equal("zip viejo", File.ReadAllText(Path.Combine(output, zipName)));
        Assert.False(File.Exists(Path.Combine(output, $"informe_forense_{safe}.pdf")));
    }

    [Fact]
    public async Task Failure_DeletesDocx_AndRethrows()
    {
        var images = Path.Combine(_root, "images3");
        var output = Path.Combine(_root, "output3");
        Directory.CreateDirectory(images); // sin la captura: el cuerpo la referencia → falla
        Directory.CreateDirectory(output);
        var cas = AgentCase();
        var sha = new string('1', 64);
        await Assert.ThrowsAnyAsync<Exception>(() => Service().GenerateReportAsync(cas,
            [new FileInfoDto(Shot, 100, sha, DateTime.UtcNow)], new Dictionary<string, string> { [Shot] = sha },
            images, output, ReportService.ZipFilenameFor(cas), new string('f', 64)));
        Assert.Empty(Directory.GetFiles(output));
    }

    [Fact]
    public void ZipFilenameFor_SameFormulaAsOldFlow()
    {
        var cas = MakeCase();
        Assert.Equal($"evidencia_{ReportService.Sanitize($"{cas.NroReferencia}_{cas.NombreDenunciante}")}.zip",
            ReportService.ZipFilenameFor(cas));
    }
}
