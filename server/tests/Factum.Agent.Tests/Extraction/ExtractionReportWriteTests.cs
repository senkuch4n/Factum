using System.Security.Cryptography;
using System.Text;
using Factum.Agent.Common;
using Factum.Agent.Models;
using Factum.Agent.Services;
using Factum.Agent.Services.Extraction;
using Factum.Agent.Services.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Tests.Extraction;

/// <summary>
/// <c>PUT /extractions/{id}/report</c> → <see cref="ExtractionService.WriteReport"/> (informe-extraccion §8.3, §11):
/// escribe <c>INFORME.docx</c> + <c>INFORME.docx.sha256</c> en la RAÍZ de la carpeta, con el hash de lo escrito, sin
/// tocar el MANIFIESTO. DataDirectory temporal propio; no toca agent-data del repo ni documentos reales.
/// </summary>
public sealed class ExtractionReportWriteTests : IDisposable
{
    private const string Id = "d3f2a9c1-0000-4000-8000-000000000001";

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("factum-ext-report");
    private readonly ExtractionService _service;
    private readonly string _folder;

    public ExtractionReportWriteTests()
    {
        var io = Options.Create(new AgentOptions { Mock = true, DataDirectory = _root.FullName });
        var dest = new DestinationService(io, new FakeDiskProbe(500L * 1024 * 1024 * 1024));
        var stay = new StayAwakeKeeper(io, NullLogger<StayAwakeKeeper>.Instance);
        _service = new ExtractionService(io, new FakeEventSink(), new StubDeviceDetails(), stay, dest,
            new FakeFolderReveal(), new FakeHostEnvironment(), TimeProvider.System, [new FakeRecordingSource()],
            new FakeScreenProbe(), NullLogger<ExtractionService>.Instance);

        _folder = Path.Combine(_root.FullName, "evidencia");
        Directory.CreateDirectory(_folder);
    }

    public void Dispose() { try { _root.Delete(true); } catch { /* best effort */ } }

    private void Journal(string status = ExtractionStatuses.Completed)
    {
        var journal = new ExtractionJournal(_root.FullName);
        journal.SaveAsync(new ExtractionJournalDoc
        {
            FolderPath = _folder,
            WorkDir = _folder,
            Snapshot = new ExtractionSnapshot { Id = Id, Status = status, FolderPath = _folder },
        }).GetAwaiter().GetResult();
    }

    private void Manifest(byte[] content)
    {
        File.WriteAllBytes(Path.Combine(_folder, "MANIFIESTO.tsv"), content);
        File.WriteAllText(Path.Combine(_folder, "MANIFIESTO.tsv.sha256"), "deadbeef  MANIFIESTO.tsv\n");
    }

    private static ExtractionReportRequest Req(byte[] docx) =>
        new(Convert.ToBase64String(docx), "INFORME.docx");

    private static readonly byte[] Docx = Encoding.UTF8.GetBytes("PK\x03\x04 contenido de prueba del informe");

    [Fact]
    public void EscribeInformeYHashEnLaRaiz_ConElHashDeLoEscrito()
    {
        Journal();
        var res = _service.WriteReport(Id, Req(Docx));

        Assert.Equal(_folder, res.Directory);
        Assert.Equal("INFORME.docx", res.Filename);
        Assert.Equal(Docx.LongLength, res.Bytes);

        var docxPath = Path.Combine(_folder, "INFORME.docx");
        var shaPath = Path.Combine(_folder, "INFORME.docx.sha256");
        Assert.Equal(Docx, File.ReadAllBytes(docxPath));

        var expected = Convert.ToHexString(SHA256.HashData(Docx)).ToLowerInvariant();
        Assert.Equal(expected, res.Sha256);
        Assert.Equal($"{expected}  INFORME.docx\n", File.ReadAllText(shaPath));
        Assert.Empty(Directory.GetFiles(_folder, "*.part"));
        Assert.NotNull(_service.Get(Id)!.ReportWrittenAt);
    }

    [Fact]
    public void NoModificaElManifiestoPreexistente()
    {
        var manifest = Encoding.UTF8.GetBytes("archivo\thash\tbytes\nfoto.jpg\tabc\t123\n");
        Manifest(manifest);
        Journal();

        var before = File.ReadAllBytes(Path.Combine(_folder, "MANIFIESTO.tsv"));
        var beforeSha = File.ReadAllText(Path.Combine(_folder, "MANIFIESTO.tsv.sha256"));
        _service.WriteReport(Id, Req(Docx));

        Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(_folder, "MANIFIESTO.tsv")));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(_folder, "MANIFIESTO.tsv")));
        Assert.Equal(beforeSha, File.ReadAllText(Path.Combine(_folder, "MANIFIESTO.tsv.sha256")));
    }

    [Fact]
    public void RegenerarSobrescribeYRecalculaLaHuella()
    {
        Journal();
        _service.WriteReport(Id, Req(Docx));

        var otro = Encoding.UTF8.GetBytes("PK\x03\x04 otra version del informe, mas larga");
        var res = _service.WriteReport(Id, Req(otro));

        Assert.Equal(otro, File.ReadAllBytes(Path.Combine(_folder, "INFORME.docx")));
        var expected = Convert.ToHexString(SHA256.HashData(otro)).ToLowerInvariant();
        Assert.Equal(expected, res.Sha256);
        Assert.Equal($"{expected}  INFORME.docx\n", File.ReadAllText(Path.Combine(_folder, "INFORME.docx.sha256")));
    }

    [Fact]
    public void RechazaBase64Invalido()
    {
        Journal();
        var ex = Assert.Throws<AgentHttpException>(() => _service.WriteReport(Id, new ExtractionReportRequest("no-es-base64!!")));
        Assert.Equal(400, ex.Status);
        Assert.Equal(AgentErrorCodes.InvalidRequest, ex.Code);
    }

    [Fact]
    public void RechazaVacio()
    {
        Journal();
        var ex = Assert.Throws<AgentHttpException>(() => _service.WriteReport(Id, new ExtractionReportRequest("")));
        Assert.Equal(400, ex.Status);
    }

    [Fact]
    public void RechazaMayorA20MiB()
    {
        Journal();
        var big = new byte[20 * 1024 * 1024 + 1];
        var ex = Assert.Throws<AgentHttpException>(() => _service.WriteReport(Id, Req(big)));
        Assert.Equal(400, ex.Status);
        Assert.Equal(AgentErrorCodes.InvalidRequest, ex.Code);
    }

    [Fact]
    public void Rechaza409SiLaExtraccionSigueCorriendo()
    {
        Journal(ExtractionStatuses.Running);
        var ex = Assert.Throws<AgentHttpException>(() => _service.WriteReport(Id, Req(Docx)));
        Assert.Equal(409, ex.Status);
        Assert.Equal(AgentErrorCodes.ExtractionNotFinished, ex.Code);
    }

    [Fact]
    public void Rechaza404SiNoExiste()
    {
        var ex = Assert.Throws<AgentHttpException>(() => _service.WriteReport("no-existe", Req(Docx)));
        Assert.Equal(404, ex.Status);
        Assert.Equal(AgentErrorCodes.ExtractionNotFound, ex.Code);
    }
}
