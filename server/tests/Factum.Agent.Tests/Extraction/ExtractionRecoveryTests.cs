using Factum.Agent.Models;
using Factum.Agent.Services.Extraction;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Tests.Extraction;

/// <summary>
/// extraccion-logica-android §5.3/§12.1: un diario en <c>running</c> al arrancar pasa a <c>interrupted /
/// agent_stopped</c>, con el manifiesto cerrado y el ACTA parcial.
/// </summary>
public class ExtractionRecoveryTests
{
    [Fact]
    public async Task RunningAlArrancar_PasaAInterrupted()
    {
        var root = Directory.CreateTempSubdirectory("factum-recovery");
        try
        {
            var folder = root.CreateSubdirectory("Factum_Extraccion_x").FullName;
            var work = Path.Combine(folder, ".factum");
            Directory.CreateDirectory(work);
            // Manifiesto parcial con una fila (como si se hubiera cortado a mitad).
            await using (var mw = new ExtractionManifestWriter(work))
                await mw.AppendAsync(new ManifestEntry("datos/crudo/contacts.txt", "", "content query", "contacts",
                    ManifestTypes.Evidence, 10, null, new string('a', 64), null, ManifestMatch.NotApplicable, false,
                    ManifestStates.Ok, null));

            var journal = new ExtractionJournal(root.FullName);
            var id = Guid.NewGuid().ToString("D");
            await journal.SaveAsync(new ExtractionJournalDoc
            {
                FolderPath = folder,
                WorkDir = work,
                Snapshot = new ExtractionSnapshot
                {
                    Id = id, Status = ExtractionStatuses.Running, Phase = ExtractionPhases.Copying,
                    Serial = "S", Manufacturer = "samsung", Model = "SM-M127F",
                    Operator = new ExtractionOperator { Dni = "1", Name = "Ana" }, FolderPath = folder,
                    StartedAt = DateTime.UtcNow.AddMinutes(-5),
                    VerifyOnDevice = true, DeviceSha256Available = true, // la verificación estaba pedida y era posible…
                    Categories = [new CategorySnapshot { Key = "contacts", Kind = "structured", Status = CategoryStatuses.Running }],
                },
            });

            var recovery = new ExtractionRecovery(Options.Create(new AgentOptions { DataDirectory = root.FullName }),
                NullLogger<ExtractionRecovery>.Instance);
            await recovery.RunAsync();

            var doc = journal.LoadAll().Single(d => d.Snapshot.Id == id);
            Assert.Equal(ExtractionStatuses.Interrupted, doc.Snapshot.Status);
            Assert.Equal("agent_stopped", doc.Snapshot.Interruption!.ReasonCode);
            Assert.Equal(CategoryStatuses.NotExtracted, doc.Snapshot.Categories[0].Status);
            Assert.True(File.Exists(Path.Combine(folder, "MANIFIESTO.tsv")));
            Assert.True(File.Exists(Path.Combine(folder, "ACTA.txt")));
            Assert.NotNull(doc.Snapshot.ManifestSha256);

            // H1 (reintento 2): aunque el equipo tenía sha256sum y se pidió verificar, la corrida se cortó: el ACTA
            // §6 tiene que decir que NO se calculó (no afirmar algo falso), no mencionar binario.
            var acta = await File.ReadAllTextAsync(Path.Combine(folder, "ACTA.txt"));
            Assert.Contains("No se calculó el SHA-256 en el dispositivo", acta);
            Assert.DoesNotContain("se calculó también en el dispositivo", acta);
        }
        finally { root.Delete(true); }
    }
}
