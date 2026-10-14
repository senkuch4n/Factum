using Factum.Backend.Models;
using Factum.Backend.Services.Timestamping;

namespace Factum.Backend.Tests.Timestamping;

/// <summary><see cref="TimestampReadme"/> (sellado-tiempo §10, §11.2) con datos ficticios.</summary>
public sealed class TimestampReadmeTests
{
    private const string ZipName = "evidencia_123_2026_Perez.zip";
    private const string DocxName = "informe_pericial_123_2026_Perez.docx";
    private static readonly DateTime Now = new(2026, 10, 14, 12, 46, 5, DateTimeKind.Utc);
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("AR", TimeSpan.FromHours(-3), "AR", "AR");

    private static CaseTimestamp Sealed(string target, string filename, char sha, string url, string authority,
        bool? inReport = null) => new()
    {
        CaseId = "c1",
        Target = target,
        Filename = filename,
        Sha256 = new string(sha, 64),
        Status = TimestampStatuses.Granted,
        Authority = authority,
        TsaUrl = url,
        GenTime = Now.AddMinutes(-5),
        SerialNumber = "0A1B",
        Tsr = [1],
        InReport = inReport,
    };

    private static string Build(params CaseTimestamp[] sealedItems) =>
        TimestampReadme.Build("123/2026", Now, Zone, ZipName, DocxName,
            new TimestampSelection(sealedItems, [new UnsealedEntry("audio.m4a", TimestampBundle.ReasonPending)]));

    [Fact]
    public void CrlfEnTodasLasLineas_YEncabezado()
    {
        var text = Build(Sealed(TimestampTargets.Zip, ZipName, 'z', "https://freetsa.org/tsr", "Free TSA", true));
        Assert.DoesNotContain("\n", text.Replace("\r\n", ""));
        Assert.DoesNotContain("\r", text.Replace("\r\n", ""));
        Assert.StartsWith("VERIFICACIÓN DE LOS SELLOS DE TIEMPO (RFC 3161)\r\n=====", text);
        Assert.Contains("Caso: 123/2026\r\n", text);
        Assert.Contains("Archivo generado por Factum el 2026-10-14 12:46:05 UTC (14/10/2026 09:46:05 (UTC-03:00)).", text);
    }

    [Fact]
    public void ComandosExactos_PorCadaSello()
    {
        var text = Build(
            Sealed(TimestampTargets.Zip, ZipName, 'z', "https://freetsa.org/tsr", "Free TSA", true),
            Sealed(TimestampTargets.Report, DocxName, 'r', "http://timestamp.digicert.com/", "DigiCert"),
            Sealed(TimestampTargets.Evidence, "captura_1.png", 'e', "https://freetsa.org/tsr", "Free TSA"));

        Assert.Contains($"openssl ts -verify -data \"{ZipName}\" -in \"sellos/{ZipName}.tsr\" -CAfile \"sellos/ca-freetsa.pem\"", text);
        Assert.Contains($"openssl ts -verify -data \"{DocxName}\" -in \"sellos/{DocxName}.tsr\" -CAfile \"sellos/ca-digicert.pem\"", text);
        Assert.Contains($"openssl ts -verify -digest {new string('e', 64)} -in \"sellos/captura_1.png.tsr\" -CAfile \"sellos/ca-freetsa.pem\"", text);
        Assert.Contains("  Tipo: Contenedor ZIP\r\n", text);
        Assert.Contains("  Tipo: Informe\r\n", text);
        Assert.Contains("  Tipo: Evidencia\r\n", text);
        Assert.Contains("  Autoridad: DigiCert (http://timestamp.digicert.com/)\r\n", text);
        Assert.Contains("  Fecha y hora certificadas: 2026-10-14 12:41:05 UTC (14/10/2026 09:41:05 (UTC-03:00))\r\n", text);
        Assert.Contains("  N.º de serie: 0A1B\r\n", text);
        Assert.Contains($"  Archivo del sello: \"sellos/captura_1.png.tsr\"\r\n", text);
        // Variantes de autoridad conocida.
        Assert.Contains("  Free TSA (freetsa.org)\r\n    1. Descargá https://freetsa.org/files/cacert.pem\r\n" +
                        "    2. Guardalo como \"sellos/ca-freetsa.pem\".\r\n", text);
        Assert.Contains("  DigiCert\r\n    1. Descargá https://cacerts.digicert.com/DigiCertTrustedRootG4.crt\r\n" +
                        "    2. Convertilo a PEM:\r\n" +
                        "         openssl x509 -inform DER -in DigiCertTrustedRootG4.crt -out \"sellos/ca-digicert.pem\"\r\n", text);
        // El ZIP figuró en el informe: sin nota.
        Assert.DoesNotContain("este sello se obtuvo después de emitir el informe", text);
    }

    [Fact]
    public void AutoridadDesconocida_VarianteGenerica()
    {
        var text = Build(Sealed(TimestampTargets.Evidence, "a.png", 'a', "https://tsa.ejemplo.com/tsr", "Ejemplo S.A."));
        Assert.Contains("  Ejemplo S.A. (tsa.ejemplo.com)\r\n", text);
        Assert.Contains("      openssl ts -reply -in \"sellos/a.png.tsr\" -token_out -out sello.tst\r\n", text);
        Assert.Contains("guardala en formato PEM como \"sellos/ca-tsa-ejemplo-com.pem\".", text);
        Assert.Contains("-CAfile \"sellos/ca-tsa-ejemplo-com.pem\"", text);
    }

    [Fact]
    public void ZipSinInReport_LlevaLaNota()
    {
        var text = Build(Sealed(TimestampTargets.Zip, ZipName, 'z', "https://freetsa.org/tsr", "Free TSA", inReport: false));
        Assert.Contains("  Nota: este sello se obtuvo después de emitir el informe; el informe indica que el contenedor no\r\n" +
                        "  tenía sello de tiempo al momento de su emisión.\r\n", text);
    }

    [Fact]
    public void ListaSinSello()
    {
        var text = Build(Sealed(TimestampTargets.Zip, ZipName, 'z', "https://freetsa.org/tsr", "Free TSA", true));
        Assert.Contains("Sin sello de tiempo:\r\n  - \"audio.m4a\": pendiente (la autoridad no respondió; se reintenta)\r\n", text);
    }

    [Fact]
    public void NingunaLineaTraeCredenciales()
    {
        // Las URLs llegan saneadas desde el sello; además se chequea que nada agregue userinfo.
        var url = TimestampingOptions.SanitizeUrl(new Uri("https://usuario:clave@tsa.privada.example/tsr"));
        var text = Build(Sealed(TimestampTargets.Evidence, "a.png", 'a', url, "Privada"));
        foreach (var line in text.Split("\r\n"))
        {
            Assert.DoesNotContain("usuario:", line);
            Assert.DoesNotContain("clave", line);
            Assert.DoesNotMatch(@"https?://[^\s""]*@", line);
        }
    }

    [Fact]
    public void SeccionesCompletas()
    {
        var text = Build(Sealed(TimestampTargets.Zip, ZipName, 'z', "https://freetsa.org/tsr", "Free TSA", true));
        foreach (var title in new[]
                 {
                     "1. CERTIFICADO RAÍZ DE LA AUTORIDAD", "2. VER LOS DATOS DE UN SELLO", "3. VERIFICAR",
                     "4. SELLOS DE ESTA CARPETA", "5. QUÉ CERTIFICA Y QUÉ NO",
                 })
            Assert.Contains(title + "\r\n", text);
        Assert.Contains("- Factum no afirma la validez legal de ninguna autoridad de sellado de tiempo en particular.\r\n", text);
        Assert.Contains($"  openssl ts -reply -in \"sellos/{ZipName}.tsr\" -text\r\n", text);
    }
}
