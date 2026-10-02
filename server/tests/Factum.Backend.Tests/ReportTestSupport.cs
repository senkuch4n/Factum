using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Branding;
using Factum.Backend.Services.Reports;
using Microsoft.Extensions.Logging.Abstractions;

namespace Factum.Backend.Tests;

/// <summary>
/// Fakes y helpers de los tests de editor-texto-enriquecido. Datos ficticios; cada generación
/// usa una carpeta temporal propia (sin Mongo ni Storage:DataDirectory).
/// </summary>
internal static class ReportTestSupport
{
    public const string V4 = "plantilla_informe_v4.docx";
    public const string V6 = "plantilla_informe_v6.docx";

    public sealed class FakeSettings : IReportSettings
    {
        public TimeZoneInfo Zone => TimeZoneInfo.Utc;
        public string ZoneLabel => "UTC";
        public string DomicilioConstituido => "";
        public bool EncryptZip => false;
        public string DefaultOperacionesRealizadas => "";
        public string DefaultAseguramientoEvidencia => "";
        public string DefaultNotasTecnicas => "";
        public string DefaultReserva => "";
    }

    public sealed class FakeBranding(BrandingSnapshot current) : IBrandingService
    {
        public BrandingSnapshot Current { get; } = current;
    }

    /// <summary>Branding con los colores de Factum (B-R2b no toca nada).</summary>
    public static BrandingSnapshot FactumBrand() =>
        new("Estudio Ficticio de Prueba", ["Calle Ficticia 123"], null, null,
            BrandingColors.DefaultPrimary, BrandingColors.DefaultAccent);

    public static Case MakeCase() => new()
    {
        NroReferencia = "4321/2026",
        NombreDenunciante = "Titular Ficticio",
        TipoCausa = "Expediente",
        Caratula = "Ficticio, Juan c/ Ejemplo S.A. s/ daños y perjuicios",
        NombreTribunal = "Juzgado Ficticio N° 1",
        ParteDenunciante = "Juan Ficticio",
        ParteDenunciada = "Ejemplo S.A.",
        ObjetoCausa = "Daños",
        AmbitoCausa = "Civil",
        FechaIntervencion = "2026-09-15",
        NombreProponente = "Dra. Proponente Ficticia",
        TipoDispositivo = "Teléfono celular",
        LineaDispositivo = "000-0000000",
        Device = new DeviceInfo { Manufacturer = "Marca", Model = "Modelo X", Imei = "000000000000000" },
        Perito = new PeritoSnapshot
        {
            Nombre = "Ana Perito Ficticia", Matricula = "9999", Profesion = "Lic. en Informática",
            Caracter = "Perito de parte",
        },
        CreatedAt = new DateTime(2026, 9, 20, 13, 30, 0, DateTimeKind.Utc),
    };

    /// <summary>Genera el informe en <paramref name="dir"/> y devuelve la ruta del DOCX.</summary>
    public static string Generate(string dir, Case cas, string template = V6, BrandingSnapshot? brand = null)
    {
        Directory.CreateDirectory(dir);
        var stamp = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        var files = new List<FileInfoDto>();
        foreach (var (name, content) in new[]
                 {
                     ("chat_export.txt", "contenido de prueba 1"),
                     ("registro.log", "contenido de prueba 2"),
                 })
        {
            var path = Path.Combine(dir, name);
            File.WriteAllText(path, content);
            File.SetLastWriteTimeUtc(path, stamp);
            files.Add(new FileInfoDto(name, content.Length, "", stamp));
        }

        var service = new ReportService(new FakeBranding(brand ?? FactumBrand()), new FakeSettings(),
            NullLogger<ReportService>.Instance, template);
        return service.GenerateAsync(cas, files, dir).GetAwaiter().GetResult().PdfPath;
    }

    public static string TextOf(OpenXmlElement e) => string.Concat(e.Descendants<Text>().Select(t => t.Text));

    // El hash del ZIP cambia entre dos generaciones (timestamps de las entradas). Se enmascara
    // en cada <w:t> antes de concatenar: después de unir, un texto vecino terminado en a-f
    // ("evidencia") se pega al hash y corre la máscara un carácter.
    private static readonly Regex Sha256 = new("^[0-9a-f]{64}$|(?<![0-9a-f])[0-9a-f]{64}(?![0-9a-f])");

    public static string TextOfMaskingHashes(OpenXmlElement e) =>
        string.Concat(e.Descendants<Text>().Select(t => Sha256.Replace(t.Text, "<hash>")));
}
