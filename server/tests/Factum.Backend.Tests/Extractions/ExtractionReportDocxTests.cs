using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Branding;
using Factum.Backend.Services.Reports;
using Microsoft.Extensions.Logging.Abstractions;

namespace Factum.Backend.Tests.Extractions;

/// <summary>
/// Generación del informe pericial de una extracción (informe-extraccion §11): cada test arma un
/// <see cref="ExtractionDto"/> ficticio, genera el DOCX real con <see cref="ReportService"/> y la plantilla
/// <c>plantilla_informe_extraccion_v6.docx</c>, y lo abre con OpenXML. No toca Mongo ni la carpeta de la extracción.
/// </summary>
public sealed class ExtractionReportDocxTests
{
    private static readonly Regex Placeholder =
        new(@"\{[#/]?[A-Za-z_][A-Za-z0-9_]*(?::[0-9.,x]+)?\}", RegexOptions.CultureInvariant);

    private static ReportService NewService() =>
        new(new ReportTestSupport.FakeBranding(ReportTestSupport.FactumBrand()),
            new ReportTestSupport.FakeSettings(), NullLogger<ReportService>.Instance);

    private static readonly User Ana = new() { Dni = "30111222", Name = "Ana Pérez" };
    private static readonly DateTime T0 = new(2026, 10, 25, 15, 0, 0, DateTimeKind.Utc);

    private static DeviceDetails SampleDetails() => new()
    {
        ReadAt = T0,
        Platform = "android",
        ConnectionId = "R58ABCDEF",
        Fields = new Dictionary<string, DeviceDetailField>
        {
            [DeviceDetailKeys.OsVersion] = new() { Value = "11" },
            [DeviceDetailKeys.SerialNumber] = new() { Value = "R58ABCDEF" },
            [DeviceDetailKeys.Kernel] = new() { Value = "5.4.0" },
            [DeviceDetailKeys.WifiState] = new() { Value = "disabled" },
        },
        ClockStart = new ClockMeasurement { Kind = ClockKinds.Start, RecordedAt = T0, DeviceMinusPcMs = 0, MarginMs = 500 },
    };

    private static ExtractionDto Dto(
        string status = ExtractionStatuses.Completed,
        string? caseRef = "1234/2026",
        TimestampDto? timestamp = null,
        ExtractionInterruptionDto? interruption = null,
        DeviceDetails? details = null,
        List<ExtractionCategoryDto>? categories = null)
    {
        categories ??=
        [
            new("contacts", "structured", ExtractionCategoryStatuses.Done, Count: 128, Method: "content query content://contacts"),
            new("call_log", "structured", ExtractionCategoryStatuses.NotExtracted, ReasonCode: "connector_install_blocked",
                Method: "app auxiliar firmada"),
            new("media", "files", ExtractionCategoryStatuses.Done, Files: 42, Bytes: 10_485_760, Method: "/sdcard/DCIM"),
        ];
        return new ExtractionDto(
            Id: "d1", Status: status, Officer: Ana, Hostname: "PERICIA-PC", OsUser: "perito",
            AgentVersion: "2.3.0", AgentMode: "installed",
            StartedAtPc: T0, FinishedAtPc: T0.AddMinutes(12), CreatedAt: T0, UpdatedAt: T0.AddMinutes(12),
            FinalRegisteredAt: T0.AddMinutes(12), PcMinusServerMs: null, PcServerMarginMs: null,
            Device: new ExtractionDeviceDto(Serial: "R58ABCDEF", Manufacturer: "samsung", Model: "SM-M127F", Platform: "android"),
            DeviceDetails: details,
            Categories: categories,
            CustomPathsCount: 0, VerifyOnDevice: true, DeviceSha256Available: true,
            Totals: new ExtractionTotalsDto(Files: 42, Bytes: 10_485_760, Errors: 0, HashMismatches: 0),
            DestinationPath: "/home/perito/Evidencia", FolderName: "2026_samsung_SM-M127F",
            ManifestSha256: "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            ManifestEntries: 170,
            DeviceModifications: [],
            Interruption: interruption,
            CaseId: caseRef is null ? null : "case-1", CaseRef: caseRef, LinkedAt: null,
            ManifestTimestamp: timestamp);
    }

    private static TimestampDto GrantedSeal() => new(
        Id: "t1", Target: "manifest", Filename: "MANIFIESTO.tsv", Sha256: "abc", Status: TimestampStatuses.Granted,
        Attempts: 1, RequestedAt: T0, NextAttemptAt: null, LastError: null, Authority: "Autoridad de Sellado de Prueba",
        TsaUrl: "https://tsa.ejemplo", GenTime: T0, GenTimeDisplay: "25/10/2026, 18:05", SerialNumber: "A1B2C3",
        PolicyOid: null, GrantedAt: T0, InReport: null);

    private static (string All, List<string> Body) OpenTexts(byte[] docx)
    {
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, false);
        var main = doc.MainDocumentPart!;
        var parts = new List<DocumentFormat.OpenXml.OpenXmlElement> { main.Document };
        parts.AddRange(main.HeaderParts.Select(h => (DocumentFormat.OpenXml.OpenXmlElement)h.Header));
        parts.AddRange(main.FooterParts.Select(f => (DocumentFormat.OpenXml.OpenXmlElement)f.Footer));
        var all = string.Concat(parts.SelectMany(p => p.Descendants<Text>()).Select(t => t.Text));
        var bodyTexts = main.Document.Body!.Descendants<Text>().Select(t => t.Text).ToList();
        return (all, bodyTexts);
    }

    private static byte[] Generate(ExtractionDto dto) =>
        NewService().GenerateExtractionReportAsync(dto).GetAwaiter().GetResult().Docx;

    [Fact]
    public void NoQuedanPlaceholdersSinReemplazar()
    {
        var (all, _) = OpenTexts(Generate(Dto(details: SampleDetails(), timestamp: GrantedSeal())));
        var leftovers = Placeholder.Matches(all).Select(m => m.Value).Distinct().ToList();
        Assert.True(leftovers.Count == 0, "placeholders sin reemplazar: " + string.Join(", ", leftovers));
    }

    [Fact]
    public void TieneElPieRealizadoConFactum()
    {
        var (all, _) = OpenTexts(Generate(Dto()));
        Assert.Contains("Realizado con Factum", all);
    }

    [Fact]
    public void TieneLaSeccionAlcancesConElTextoFijo()
    {
        var (all, _) = OpenTexts(Generate(Dto()));
        Assert.Contains("Alcances y limitaciones", all);
        Assert.Contains("No cubre:", all);
        Assert.Contains("no una limitación de esta herramienta ni un pendiente de desarrollo", all);
    }

    [Fact]
    public void LaTablaDeCategoriasTieneUnaFilaPorCategoria()
    {
        var categories = new List<ExtractionCategoryDto>
        {
            new("contacts", "structured", ExtractionCategoryStatuses.Done, Count: 10, Method: "query a"),
            new("sms_mms", "structured", ExtractionCategoryStatuses.Done, Count: 20, Method: "query b"),
            new("media", "files", ExtractionCategoryStatuses.Done, Files: 5, Bytes: 1024, Method: "/sdcard/x"),
        };
        using var ms = new MemoryStream(Generate(Dto(categories: categories)));
        using var doc = WordprocessingDocument.Open(ms, false);
        var table = doc.MainDocumentPart!.Document.Body!.Descendants<Table>()
            .First(t => string.Concat(t.Descendants<Text>().Select(x => x.Text)).Contains("Resultado")
                        && string.Concat(t.Descendants<Text>().Select(x => x.Text)).Contains("Categoría"));
        // Encabezado (1) + una fila por categoría (3).
        Assert.Equal(1 + categories.Count, table.Elements<TableRow>().Count());
        var text = string.Concat(table.Descendants<Text>().Select(x => x.Text));
        Assert.Contains("Contactos", text);
        Assert.Contains("SMS / MMS", text);
        Assert.Contains("Multimedia", text);
    }

    [Fact]
    public void SinCaso_NoApareceLaActuacion_YSeGeneraIgual()
    {
        var (all, _) = OpenTexts(Generate(Dto(caseRef: null)));
        Assert.DoesNotContain("Actuación asociada", all);
        Assert.Contains("Identificación", all); // el informe se generó
    }

    [Fact]
    public void ConCaso_CitaLaActuacion()
    {
        var (all, _) = OpenTexts(Generate(Dto(caseRef: "1234/2026")));
        Assert.Contains("Actuación asociada", all);
        Assert.Contains("1234/2026", all);
    }

    [Fact]
    public void Interrumpida_ApareceLaSeccionConElMotivo()
    {
        var dto = Dto(status: ExtractionStatuses.Interrupted,
            interruption: new ExtractionInterruptionDto("device_disconnected", "Se desconectó el cable", T0.AddMinutes(5)));
        var (all, _) = OpenTexts(Generate(dto));
        Assert.Contains("Interrupción o cancelación", all);
        Assert.Contains("finalizó de forma anticipada", all);
        Assert.Contains("Se desconectó el cable", all);
    }

    [Fact]
    public void Terminada_SinInterrupcion_NoApareceLaSeccion()
    {
        var (all, _) = OpenTexts(Generate(Dto()));
        Assert.DoesNotContain("Interrupción o cancelación", all);
    }

    [Fact]
    public void SelloNull_SinBloqueDeSello_SelloGranted_ConAutoridadYFecha()
    {
        var (sinSello, _) = OpenTexts(Generate(Dto(timestamp: null)));
        Assert.DoesNotContain("fecha cierta de la integridad del conjunto", sinSello);
        Assert.Contains("no cuenta con sello de tiempo de un tercero", sinSello);

        var (conSello, _) = OpenTexts(Generate(Dto(timestamp: GrantedSeal())));
        Assert.Contains("Autoridad de Sellado de Prueba", conSello);
        Assert.Contains("25/10/2026, 18:05", conSello);
        Assert.Contains("fecha cierta de la integridad del conjunto", conSello);
    }

    [Fact]
    public void ElDocxEsValidoSegunElEsquemaOpenXml()
    {
        using var ms = new MemoryStream(Generate(Dto(details: SampleDetails(), timestamp: GrantedSeal())));
        using var doc = WordprocessingDocument.Open(ms, false);
        var validator = new OpenXmlValidator(FileFormatVersions.Office2019);
        var errors = validator.Validate(doc)
            .Select(e => $"{e.Part?.Uri} {e.Id} {e.Description}")
            .Distinct()
            .ToList();
        Assert.True(errors.Count == 0, "errores de validación OpenXML:\n" + string.Join("\n", errors));
    }

    [Fact]
    public void FichaTecnica_DesapareceSinDeviceDetails()
    {
        var (sinFicha, _) = OpenTexts(Generate(Dto(details: null)));
        Assert.DoesNotContain("Sistema operativo", sinFicha);

        var (conFicha, _) = OpenTexts(Generate(Dto(details: SampleDetails())));
        Assert.Contains("Sistema operativo", conFicha);
        Assert.Contains("Android 11", conFicha);
    }
}
