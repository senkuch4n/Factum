using System.IO.Compression;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Factum.Backend.Models;
using static Factum.Backend.Tests.ReportTestSupport;

namespace Factum.Backend.Tests.FichaTecnica;

/// <summary>
/// Ficha técnica en el informe (metadatos-dispositivo §9, §13.3) sobre la plantilla v6 real, con datos ficticios del
/// caso y la ficha real del SM-M127F. Carpetas temporales propias. Con <c>FACTUM_RENDER_DIR</c> deja las muestras.
/// </summary>
public sealed class ReportDeviceDetailsDocxTests : IDisposable
{
    private const string Intro = "Datos técnicos obtenidos automáticamente por Factum mediante consultas de solo " +
                                 "lectura al dispositivo el 14/10/2026 a las 15:22 horas:";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "factum-ficha-docx-" + Guid.NewGuid().ToString("N"));

    public ReportDeviceDetailsDocxTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private (List<string> Paragraphs, int Tables, string Xml) Render(Case cas, string name, string template = V6)
    {
        var path = Generate(Path.Combine(_root, name), cas, template);
        var render = Environment.GetEnvironmentVariable("FACTUM_RENDER_DIR");
        if (!string.IsNullOrWhiteSpace(render))
        {
            Directory.CreateDirectory(render);
            File.Copy(path, Path.Combine(render, $"ficha_{name}.docx"), overwrite: true);
        }
        using var doc = WordprocessingDocument.Open(path, false);
        var body = doc.MainDocumentPart!.Document.Body!;
        var paras = body.Descendants<Paragraph>().Select(TextOfMaskingHashes).ToList();
        var tables = body.Descendants<Table>().Count();
        // Todo el paquete (cuerpo, encabezados, pies, propiedades), para buscar el SSID en cualquier parte.
        using var zip = ZipFile.OpenRead(path);
        var xml = string.Concat(zip.Entries.Where(e => e.FullName.EndsWith(".xml"))
            .Select(e => new StreamReader(e.Open()).ReadToEnd()));
        return (paras, tables, xml);
    }

    private static Case WithFicha(string? ssid = null, bool includeSsid = false)
    {
        var cas = MakeCase();
        cas.DeviceDetails = DeviceDetailsTestData.Android(ssid, includeSsid);
        return cas;
    }

    [Fact]
    public void Sin_ficha_el_bloque_desaparece()
    {
        var (paras, _, xml) = Render(MakeCase(), "sin");
        Assert.DoesNotContain(paras, p => p.StartsWith("Datos técnicos obtenidos"));
        Assert.DoesNotContain(paras, p => p.Contains("Reloj del dispositivo"));
        Assert.DoesNotContain("fichaTecnica", xml);
        Assert.DoesNotContain("{ficha", xml);
        Assert.DoesNotContain("{descripcionRelojDispositivo}", xml);
    }

    [Fact]
    public void Con_ficha_aparece_el_bloque_y_el_resto_queda_igual()
    {
        var (without, tablesWithout, _) = Render(MakeCase(), "base");
        var (with, tablesWith, xml) = Render(WithFicha(), "con");

        Assert.Equal(tablesWithout + 1, tablesWith);
        Assert.Contains(Intro, with);
        foreach (var label in new[]
                 {
                     "Sistema operativo:", "Compilación:", "Parche de seguridad:", "Número de serie:",
                     "Identificador de conexión:", "Kernel:", "Operador / SIM:", "Estado de arranque:",
                     "Zona horaria:", "Wifi:",
                 })
            Assert.Contains(label, with);
        Assert.Contains("verifiedbootstate: orange; flash.locked: 0; vbmeta.device_state: unlocked", with);
        Assert.Contains("Android 11 (nivel de API 30)", with);
        Assert.Contains(with, p => p.Contains("el dispositivo adelantaba 10 s respecto de la estación de trabajo (± 1 s)."));
        Assert.Contains("Zona horaria configurada en el dispositivo: America/Argentina/Buenos_Aires.", with);
        Assert.DoesNotContain("{ficha", xml);
        Assert.DoesNotContain("fichaTecnica", xml);

        // Fuera del bloque, el informe es el mismo de siempre.
        var i = with.IndexOf(Intro);
        var j = with.FindIndex(p => p.StartsWith("Zona horaria configurada en el dispositivo"));
        Assert.True(i > 0 && j > i);
        var outside = with.Take(i).Concat(with.Skip(j + 1)).ToList();
        Assert.Equal(without, outside);

        // El bloque va después de "Titular" y antes de las capturas del IMEI.
        var titular = with.FindIndex(p => p.StartsWith("Titular"));
        Assert.True(titular >= 0 && titular < i);
    }

    [Fact]
    public void Un_SSID_con_placeholder_sale_literal()
    {
        var (paras, _, _) = Render(WithFicha("{caratula}", includeSsid: true), "ssid-literal");
        Assert.Contains("Conectado a la red «{caratula}»", paras);
    }

    [Fact]
    public void Sin_el_flag_el_SSID_no_aparece_en_ningun_lado()
    {
        var (paras, _, xml) = Render(WithFicha("Red Secreta Ficticia"), "ssid-oculto");
        Assert.Contains("Conectado (el nombre de la red no se consigna)", paras);
        Assert.DoesNotContain("Red Secreta Ficticia", xml);
    }

    [Fact]
    public void La_frase_de_umbral_sale_en_el_informe()
    {
        var cas = WithFicha();
        cas.DeviceDetails!.ClockStart!.DeviceMinusPcMs = 90000;
        var (paras, _, _) = Render(cas, "umbral");
        Assert.Contains("Se deja constancia de que el reloj del dispositivo presentaba una diferencia de 1 min 30 s " +
                        "respecto de la estación de trabajo; las horas visibles en las capturas corresponden al reloj " +
                        "del dispositivo.", paras);
    }

    [Fact]
    public void Plantilla_v4_tambien_resuelve_el_bloque()
    {
        var (paras, _, xml) = Render(WithFicha(), "v4", V4);
        Assert.Contains(Intro, paras);
        Assert.Contains(paras, p => p == "Sistema operativo: Android 11 (nivel de API 30)");
        Assert.DoesNotContain("{ficha", xml);
    }
}
