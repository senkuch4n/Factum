using Factum.Agent.Common;
using Factum.Agent.Models;
using Factum.Agent.Services.Extraction;
using Factum.Agent.Services.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Tests.Extraction;

/// <summary>
/// Extracción simulada de punta a punta SIN celular (extraccion-logica-android §5.12, §14): los 5
/// <c>MockExtractionOutcome</c> dejan la carpeta con MANIFIESTO.tsv + .sha256 + SHA256SUMS + ACTA.* + CIERRE + LEEME,
/// y el estado terminal correcto.
/// </summary>
public sealed class ExtractionServiceMockTests
{
    private sealed class Harness : IDisposable
    {
        public DirectoryInfo Root { get; }
        public DirectoryInfo Dest { get; }
        public FakeEventSink Sink { get; } = new();
        public ExtractionService Service { get; }

        public Harness(string? outcome)
        {
            Root = Directory.CreateTempSubdirectory("factum-ext-mock");
            Dest = Root.CreateSubdirectory("dest");
            var io = Options.Create(new AgentOptions
            {
                Mock = true, DataDirectory = Root.FullName, MockExtractionOutcome = outcome,
            });
            var dest = new DestinationService(io, new FakeDiskProbe(500L * 1024 * 1024 * 1024));
            var stay = new StayAwakeKeeper(io, NullLogger<StayAwakeKeeper>.Instance);
            Recordings = new FakeRecordingSource();
            Service = new ExtractionService(io, Sink, new StubDeviceDetails(), stay, dest, new FakeFolderReveal(),
                new FakeHostEnvironment(), TimeProvider.System, [Recordings], new FakeScreenProbe(),
                NullLogger<ExtractionService>.Instance);
        }

        public FakeRecordingSource Recordings { get; private set; } = new();

        public void Dispose() { try { Root.Delete(true); } catch { } }
    }

    private static ExtractionStartRequest Req(string dest) => new(
        Serial: "MOCK1",
        StructuredCategories: ["contacts", "sms_mms", "calendar", "apps", "call_log"],
        FileCategories: ["media"],
        DestinationPath: dest,
        VerifyOnDevice: true,
        ConfirmDeviceModifications: true,
        Operator: new ExtractionOperatorDto("30111222", "Ana Pérez"));

    private static async Task<ExtractionSnapshot> WaitTerminalAsync(ExtractionService svc, string id, int timeoutMs = 15000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var s = svc.Get(id);
            if (s is not null && ExtractionStatuses.IsTerminal(s.Status)) return s;
            await Task.Delay(50);
        }
        throw new TimeoutException("La extracción no terminó a tiempo.");
    }

    // §14: `sha256sum -c SHA256SUMS.txt` tiene que pasar. Se verifica en proceso (sin tool externa).
    private static async Task AssertSha256SumsVerifyAsync(string folder)
    {
        var lines = await File.ReadAllLinesAsync(Path.Combine(folder, "SHA256SUMS.txt"));
        Assert.NotEmpty(lines);
        foreach (var line in lines.Where(l => l.Length > 0))
        {
            var parts = line.Split("  ", 2);
            Assert.Equal(2, parts.Length);
            var (expected, rel) = (parts[0], parts[1]);
            var actual = await Factum.Agent.Common.FileHashing.Sha256Async(Path.Combine(folder, rel));
            Assert.Equal(expected, actual);
        }
        // Y el hash del propio manifiesto coincide con MANIFIESTO.tsv.sha256.
        var shaLine = (await File.ReadAllTextAsync(Path.Combine(folder, "MANIFIESTO.tsv.sha256"))).Split("  ")[0];
        Assert.Equal(await Factum.Agent.Common.FileHashing.Sha256Async(Path.Combine(folder, "MANIFIESTO.tsv")), shaLine);
    }

    private static void AssertClosingFiles(string folder)
    {
        foreach (var f in new[] { "MANIFIESTO.tsv", "MANIFIESTO.tsv.sha256", "SHA256SUMS.txt", "ACTA.txt",
                     "ACTA.json", "CIERRE.txt", "LEEME.txt" })
            Assert.True(File.Exists(Path.Combine(folder, f)), $"falta {f}");
    }

    [Fact]
    public async Task Completed_DejaLaCarpetaYEstadoCompleted()
    {
        using var h = new Harness(MockExtraction.Outcomes.Completed);
        var snap = h.Service.Start(Req(h.Dest.FullName));
        Assert.Equal(ExtractionStatuses.Running, snap.Status);

        var terminal = await WaitTerminalAsync(h.Service, snap.Id);
        Assert.Equal(ExtractionStatuses.Completed, terminal.Status);
        AssertClosingFiles(terminal.FolderPath);
        Assert.NotNull(terminal.ManifestSha256);
        Assert.NotNull(terminal.Record);
        await AssertSha256SumsVerifyAsync(terminal.FolderPath);
        // call_log se extrajo (mock) y hubo modificaciones del connector.
        Assert.Contains(terminal.DeviceModifications, m => m.Kind == DeviceModificationKinds.ConnectorInstalled);
        // Evento finished emitido.
        Assert.Contains(h.Sink.Snapshot(), e => e.Type == "extraction_finished");
    }

    [Fact]
    public async Task CompletedWithErrors()
    {
        using var h = new Harness(MockExtraction.Outcomes.CompletedWithErrors);
        var snap = h.Service.Start(Req(h.Dest.FullName));
        var terminal = await WaitTerminalAsync(h.Service, snap.Id);
        Assert.Equal(ExtractionStatuses.CompletedWithErrors, terminal.Status);
        AssertClosingFiles(terminal.FolderPath);
    }

    [Fact]
    public async Task ConnectorBlocked_CallLogNotExtracted()
    {
        using var h = new Harness(MockExtraction.Outcomes.ConnectorBlocked);
        var snap = h.Service.Start(Req(h.Dest.FullName));
        var terminal = await WaitTerminalAsync(h.Service, snap.Id);
        Assert.Equal(ExtractionStatuses.CompletedWithErrors, terminal.Status);
        var callLog = terminal.Categories.First(c => c.Key == "call_log");
        Assert.Equal(CategoryStatuses.NotExtracted, callLog.Status);
        Assert.Equal("connector_install_blocked", callLog.ReasonCode);
        // Sin modificaciones: no se instaló nada.
        Assert.DoesNotContain(terminal.DeviceModifications, m => m.Kind == DeviceModificationKinds.ConnectorInstalled);
    }

    [Fact]
    public async Task Interrupted()
    {
        using var h = new Harness(MockExtraction.Outcomes.Interrupted);
        var snap = h.Service.Start(Req(h.Dest.FullName));
        var terminal = await WaitTerminalAsync(h.Service, snap.Id);
        Assert.Equal(ExtractionStatuses.Interrupted, terminal.Status);
        Assert.NotNull(terminal.Interruption);
        AssertClosingFiles(terminal.FolderPath);
    }

    [Fact]
    public async Task CancelledSlow_SePuedeCancelar()
    {
        using var h = new Harness(MockExtraction.Outcomes.CancelledSlow);
        var snap = h.Service.Start(Req(h.Dest.FullName));
        // ActiveOperations informa extraction_android mientras corre.
        Assert.Contains(h.Service.ActiveOperations, op => op.Kind == ExtractionService.OperationKind);

        // Esperá a que entre a la copia lenta y cancelá.
        await Task.Delay(300);
        h.Service.Cancel(snap.Id);
        var terminal = await WaitTerminalAsync(h.Service, snap.Id);
        Assert.Equal(ExtractionStatuses.Cancelled, terminal.Status);
        AssertClosingFiles(terminal.FolderPath);
    }

    [Fact]
    public void DeviceBusy_SiElSerialEstaGrabando_409()
    {
        using var h = new Harness(MockExtraction.Outcomes.Completed);
        h.Recordings.Recording("MOCK1"); // el mismo serial del pedido
        var ex = Assert.Throws<Factum.Agent.Services.AgentHttpException>(() => h.Service.Start(Req(h.Dest.FullName)));
        Assert.Equal(409, ex.Status);
        Assert.Equal(AgentErrorCodes.DeviceBusy, ex.Code);
    }

    [Fact]
    public async Task Acta_DelResultadoReal_IdentificaElEquipo()
    {
        using var h = new Harness(MockExtraction.Outcomes.Completed);
        var snap = h.Service.Start(Req(h.Dest.FullName));
        var terminal = await WaitTerminalAsync(h.Service, snap.Id);
        var actaTxt = await File.ReadAllTextAsync(Path.Combine(terminal.FolderPath, "ACTA.txt"));
        Assert.Contains("samsung", actaTxt);
        Assert.Contains("SM-M127F", actaTxt);
        Assert.Contains("MOCK1", actaTxt); // serial de conexión
        using var json = System.Text.Json.JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(terminal.FolderPath, "ACTA.json")));
        Assert.Equal("MOCK1", json.RootElement.GetProperty("device_identity").GetProperty("serial").GetString());
    }

    [Fact]
    public async Task SegundaExtraccionEnCurso_Da409()
    {
        using var h = new Harness(MockExtraction.Outcomes.CancelledSlow);
        var first = h.Service.Start(Req(h.Dest.FullName));
        var ex = Assert.Throws<Factum.Agent.Services.AgentHttpException>(() => h.Service.Start(Req(h.Dest.FullName)));
        Assert.Equal(409, ex.Status);
        Assert.Equal("extraction_in_progress", ex.Code);
        h.Service.Cancel(first.Id);
        await WaitTerminalAsync(h.Service, first.Id);
    }
}
