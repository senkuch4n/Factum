using System.Text;
using Factum.Agent.Common;
using Factum.Agent.Models;
using Factum.Agent.Services;

namespace Factum.Agent.Tests;

/// <summary>
/// <c>PUT /cases/{id}/zip/seals</c> (sellado-tiempo §7.3, §11.3) sobre <see cref="CaseZipService"/>. DataDirectory y
/// EvidenceDirectory de prueba propios (Path.GetTempPath()/&lt;guid&gt;), nunca la carpeta default ni agent-data del repo.
/// </summary>
public sealed class CaseSealsTests : IDisposable
{
    private const string CaseId = "3f2a9c1e-0000-4000-8000-000000000001";
    private const string CaseRef = "1234/2026";
    private const string ZipName = "evidencia_1234_2026_Titular.zip";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "factum-agent-seals-" + Guid.NewGuid().ToString("N"));
    private readonly CaseZipService _zips;

    public CaseSealsTests()
    {
        var opts = new AgentOptions
        {
            DataDirectory = Path.Combine(_root, "agent-data"),
            EvidenceDirectory = Path.Combine(_root, "Evidencia"),
            MinFreeBytes = 0,
        };
        _zips = new CaseZipService(opts, new CaseEvidenceStore(opts));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string Final => Path.Combine(_root, "Evidencia", "1234_2026_3f2a9c1e");
    private string Seals => Path.Combine(Final, "sellos");

    private void FinalZip()
    {
        Directory.CreateDirectory(Final);
        File.WriteAllBytes(Path.Combine(Final, ZipName), [1, 2, 3]);
    }

    private static SealFileItem Item(string name, byte[] data) => new(name, Convert.ToBase64String(data));

    private static WriteSealsRequest Request(string? readme = "LEEME\r\nverificá", params SealFileItem[] files) =>
        new(CaseRef, ZipName, files.ToList(), readme);

    private static readonly byte[] TsrA = [0x30, 0x82, 0x01, 0x02, 0xAA];
    private static readonly byte[] TsrZip = [0x30, 0x82, 0x01, 0x02, 0xBB];

    [Fact]
    public async Task EscribeEnSellos_ConLosBytesExactos_YElLeemeSinBom()
    {
        FinalZip();
        var res = await _zips.WriteSealsAsync(CaseId,
            Request(files: [Item("captura_1.png.tsr", TsrA), Item(ZipName + ".tsr", TsrZip)]), default);

        Assert.Equal(Seals, res.Directory);
        Assert.Equal(3, res.Written);
        Assert.Equal(0, res.Unchanged);
        Assert.Equal(TsrA, File.ReadAllBytes(Path.Combine(Seals, "captura_1.png.tsr")));
        Assert.Equal(TsrZip, File.ReadAllBytes(Path.Combine(Seals, ZipName + ".tsr")));
        var readme = File.ReadAllBytes(Path.Combine(Seals, "LEEME-verificacion.txt"));
        Assert.False(readme.Length >= 3 && readme[0] == 0xEF && readme[1] == 0xBB && readme[2] == 0xBF);
        Assert.Equal("LEEME\r\nverificá", Encoding.UTF8.GetString(readme));
        Assert.Empty(Directory.GetFiles(Seals, "*.part"));
    }

    [Fact]
    public async Task SegundaLlamadaIgual_Unchanged_OtroContenido_Sobrescribe()
    {
        FinalZip();
        await _zips.WriteSealsAsync(CaseId, Request(files: Item("captura_1.png.tsr", TsrA)), default);
        var again = await _zips.WriteSealsAsync(CaseId, Request(files: Item("captura_1.png.tsr", TsrA)), default);
        Assert.Equal((0, 2), (again.Written, again.Unchanged));

        var changed = await _zips.WriteSealsAsync(CaseId,
            Request(readme: "LEEME nuevo", files: Item("captura_1.png.tsr", TsrZip)), default);
        Assert.Equal((2, 0), (changed.Written, changed.Unchanged));
        Assert.Equal(TsrZip, File.ReadAllBytes(Path.Combine(Seals, "captura_1.png.tsr")));
        Assert.Empty(Directory.GetFiles(Seals, "*.part"));
    }

    [Fact]
    public async Task SinReadme_NoEscribeElLeeme()
    {
        FinalZip();
        var res = await _zips.WriteSealsAsync(CaseId, Request(readme: null, files: Item("a.png.tsr", TsrA)), default);
        Assert.Equal(1, res.Written);
        Assert.False(File.Exists(Path.Combine(Seals, "LEEME-verificacion.txt")));
    }

    [Fact]
    public async Task SinZipFinal_404()
    {
        var ex = await Assert.ThrowsAsync<AgentHttpException>(() =>
            _zips.WriteSealsAsync(CaseId, Request(files: Item("a.png.tsr", TsrA)), default));
        Assert.Equal((404, AgentErrorCodes.ZipNotFound), (ex.Status, ex.Code));
        Assert.False(Directory.Exists(Seals));
    }

    [Fact]
    public async Task ZipFilenameInvalido_400()
    {
        FinalZip();
        var ex = await Assert.ThrowsAsync<AgentHttpException>(() =>
            _zips.WriteSealsAsync(CaseId, new WriteSealsRequest(CaseRef, "../x.zip", [], null), default));
        Assert.Equal((400, AgentErrorCodes.InvalidFilename), (ex.Status, ex.Code));
    }

    [Theory]
    [InlineData("../x.tsr")]
    [InlineData("a/b.tsr")]
    [InlineData("x.txt")]
    [InlineData("CON.tsr")]
    [InlineData("abc‮def.tsr")]
    public async Task NombreInvalido_400_SinEscribirNada(string name)
    {
        FinalZip();
        var ex = await Assert.ThrowsAsync<AgentHttpException>(() =>
            _zips.WriteSealsAsync(CaseId, Request(files: [Item("ok.png.tsr", TsrA), Item(name, TsrA)]), default));
        Assert.Equal((400, AgentErrorCodes.InvalidRequest), (ex.Status, ex.Code));
        Assert.Equal(name, ex.ToBody()["filename"]);
        Assert.False(Directory.Exists(Seals)); // valida todo antes de escribir
    }

    [Theory]
    [InlineData("evidencia_1234_2026_Titular.zip.tsr")]
    [InlineData("informe_pericial_1234_2026_Titular.docx.tsr")]
    public async Task NombresDeArtefactos_Aceptados(string name)
    {
        FinalZip();
        var res = await _zips.WriteSealsAsync(CaseId, Request(readme: null, files: Item(name, TsrA)), default);
        Assert.Equal(1, res.Written);
        Assert.True(File.Exists(Path.Combine(Seals, name)));
    }

    [Fact]
    public async Task Base64Invalido_400()
    {
        FinalZip();
        var ex = await Assert.ThrowsAsync<AgentHttpException>(() =>
            _zips.WriteSealsAsync(CaseId, Request(files: new SealFileItem("a.png.tsr", "%%%no-base64%%%")), default));
        Assert.Equal((400, AgentErrorCodes.InvalidRequest), (ex.Status, ex.Code));
    }

    [Fact]
    public async Task MasDe64KiB_O_Vacio_400()
    {
        FinalZip();
        var big = await Assert.ThrowsAsync<AgentHttpException>(() =>
            _zips.WriteSealsAsync(CaseId, Request(files: Item("a.png.tsr", new byte[64 * 1024 + 1])), default));
        Assert.Equal(AgentErrorCodes.InvalidRequest, big.Code);
        var empty = await Assert.ThrowsAsync<AgentHttpException>(() =>
            _zips.WriteSealsAsync(CaseId, Request(files: Item("a.png.tsr", [])), default));
        Assert.Equal(AgentErrorCodes.InvalidRequest, empty.Code);
        var ok = await _zips.WriteSealsAsync(CaseId, Request(readme: null, files: Item("a.png.tsr", new byte[64 * 1024])), default);
        Assert.Equal(1, ok.Written);
    }

    [Fact]
    public async Task NombresRepetidos_400()
    {
        FinalZip();
        var ex = await Assert.ThrowsAsync<AgentHttpException>(() =>
            _zips.WriteSealsAsync(CaseId, Request(files: [Item("a.png.tsr", TsrA), Item("a.png.tsr", TsrZip)]), default));
        Assert.Equal(AgentErrorCodes.InvalidRequest, ex.Code);
    }

    [Fact]
    public async Task ReadmeDeMasDe256KiB_400()
    {
        FinalZip();
        var ex = await Assert.ThrowsAsync<AgentHttpException>(() =>
            _zips.WriteSealsAsync(CaseId, Request(readme: new string('a', 256 * 1024 + 1)), default));
        Assert.Equal(AgentErrorCodes.InvalidRequest, ex.Code);
    }

    [Fact]
    public async Task LockTomado_409()
    {
        FinalZip();
        var gate = _zips.LockFor(CaseId);
        await gate.WaitAsync();
        try
        {
            var ex = await Assert.ThrowsAsync<AgentHttpException>(() =>
                _zips.WriteSealsAsync(CaseId, Request(files: Item("a.png.tsr", TsrA)), default));
            Assert.Equal((409, AgentErrorCodes.ZipInProgress), (ex.Status, ex.Code));
        }
        finally
        {
            gate.Release();
        }
    }

    [Fact]
    public void Capability_CaseSealsV1() =>
        Assert.Contains("case_seals_v1", Factum.Agent.Controllers.HealthController.Capabilities);
}
