using System.IO.Compression;
using System.Text;
using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Timestamping;

namespace Factum.Backend.Tests.Timestamping;

/// <summary><see cref="TimestampBundle"/> (sellado-tiempo §6.6, §11.2) y el mapeo a <see cref="TimestampDto"/>.</summary>
public sealed class TimestampBundleTests
{
    private const string ZipName = "evidencia_123_2026_Perez.zip";
    private const string DocxName = "informe_pericial_123_2026_Perez.docx";

    private static string Sha(char c) => new(c, 64);

    private static CaseTimestamp Ts(string target, string filename, char sha, string status = TimestampStatuses.Granted) => new()
    {
        CaseId = "c1",
        Target = target,
        Filename = filename,
        Sha256 = Sha(sha),
        Status = status,
        Tsr = status == TimestampStatuses.Granted ? [0x30, (byte)sha] : null,
        GenTime = status == TimestampStatuses.Granted ? new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc) : null,
        Authority = "Free TSA",
        TsaUrl = "https://freetsa.org/tsr",
        SerialNumber = "01",
    };

    private static Case MakeCase() => new()
    {
        Id = "c1",
        NroReferencia = "123/2026",
        ZipFilename = ZipName,
        ZipHash = Sha('z'),
        PdfFilename = DocxName,
        ReportHash = Sha('r'),
        Evidence =
        [
            new EvidenceItem { Filename = "b.png", Sha256 = Sha('b') },
            new EvidenceItem { Filename = "a.png", Sha256 = Sha('2') }, // reemplazado: el hash actual es '2'
            new EvidenceItem { Filename = "c.png", Sha256 = Sha('c') },
            new EvidenceItem { Filename = "d.png", Sha256 = Sha('d') },
        ],
    };

    [Fact]
    public void Select_SoloManifiestoActualConHashActual_ZipYInformePorHash_EnOrden()
    {
        var all = new[]
        {
            Ts(TimestampTargets.Evidence, "a.png", '1'),                 // hash viejo de a.png: no entra
            Ts(TimestampTargets.Evidence, "a.png", '2'),
            Ts(TimestampTargets.Evidence, "b.png", 'b'),
            Ts(TimestampTargets.Evidence, "c.png", 'c', TimestampStatuses.Pending),
            Ts(TimestampTargets.Evidence, "borrado.png", 'x'),           // ya no está en el manifiesto
            Ts(TimestampTargets.Zip, ZipName, 'y'),                      // ZIP de un intento descartado
            Ts(TimestampTargets.Zip, ZipName, 'z'),
            Ts(TimestampTargets.Report, DocxName, 'r'),
        };

        var sel = TimestampBundle.Select(MakeCase(), all);

        Assert.Equal([ZipName, DocxName, "a.png", "b.png"], sel.Sealed.Select(t => t.Filename).ToArray());
        Assert.Equal(Sha('z'), sel.Sealed[0].Sha256);
        Assert.Equal(Sha('2'), sel.Sealed[2].Sha256);
        Assert.Equal(["c.png", "d.png"], sel.Unsealed.Select(u => u.Filename).ToArray());
        Assert.Equal(TimestampBundle.ReasonPending, sel.Unsealed[0].Reason);
        Assert.Equal(TimestampBundle.ReasonNone, sel.Unsealed[1].Reason);
        Assert.Equal([ZipName + ".tsr", DocxName + ".tsr", "a.png.tsr", "b.png.tsr"],
            sel.Sealed.Select(TimestampBundle.TsrName).ToArray());
    }

    [Fact]
    public void Select_Borrador_SinZipNiInforme()
    {
        var cas = MakeCase();
        cas.ZipFilename = null;
        cas.ZipHash = null;
        cas.PdfFilename = null;
        cas.ReportHash = null;
        var sel = TimestampBundle.Select(cas, [Ts(TimestampTargets.Evidence, "b.png", 'b')]);
        Assert.Equal(["b.png"], sel.Sealed.Select(t => t.Filename).ToArray());
        Assert.DoesNotContain(sel.Unsealed, u => u.Filename == ZipName);
    }

    [Fact]
    public void BuildZip_TraeLosTsrYElLeeme()
    {
        var sealedItems = new[] { Ts(TimestampTargets.Zip, ZipName, 'z'), Ts(TimestampTargets.Evidence, "b.png", 'b') };
        var bytes = TimestampBundle.BuildZip(sealedItems, "LEEME\r\nlínea");

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.Equal(["sellos/" + ZipName + ".tsr", "sellos/b.png.tsr", "sellos/LEEME-verificacion.txt"],
            zip.Entries.Select(e => e.FullName).ToArray());
        using var ms = new MemoryStream();
        zip.GetEntry("sellos/b.png.tsr")!.Open().CopyTo(ms);
        Assert.Equal(new byte[] { 0x30, (byte)'b' }, ms.ToArray());
        using var readme = new MemoryStream();
        zip.GetEntry("sellos/LEEME-verificacion.txt")!.Open().CopyTo(readme);
        var raw = readme.ToArray();
        Assert.False(raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF); // sin BOM
        Assert.Equal("LEEME\r\nlínea", Encoding.UTF8.GetString(raw));
    }

    [Fact]
    public void Dto_GenTimeDisplay_EnLaZonaDelInforme_EInReport()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("AR", TimeSpan.FromHours(-3), "AR", "AR");
        var zip = Ts(TimestampTargets.Zip, ZipName, 'z');
        var dto = TimestampDto.From(zip, zone);
        Assert.Equal("14/10/2026 09:00:00 (UTC-03:00)", dto.GenTimeDisplay);
        Assert.False(dto.InReport); // granted sin InReport = obtenido después de emitir el informe
        zip.InReport = true;
        Assert.True(TimestampDto.From(zip, zone).InReport);

        var pending = Ts(TimestampTargets.Zip, ZipName, 'z', TimestampStatuses.Pending);
        pending.NextAttemptAt = new DateTime(2026, 10, 14, 12, 1, 0, DateTimeKind.Utc);
        var p = TimestampDto.From(pending, zone);
        Assert.Null(p.InReport);
        Assert.Null(p.GenTimeDisplay);
        Assert.NotNull(p.NextAttemptAt);

        Assert.Null(TimestampDto.From(Ts(TimestampTargets.Evidence, "b.png", 'b'), zone).InReport);
    }

    [Fact]
    public void Dto_JsonSnakeCase_NombresDelContrato()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(TimestampDto.From(Ts(TimestampTargets.Zip, ZipName, 'z'),
            TimeZoneInfo.Utc), new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
        });
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(
            ["id", "target", "filename", "sha256", "status", "attempts", "requested_at", "next_attempt_at",
                "last_error", "authority", "tsa_url", "gen_time", "gen_time_display", "serial_number", "policy_oid",
                "granted_at", "in_report"],
            doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
    }
}
