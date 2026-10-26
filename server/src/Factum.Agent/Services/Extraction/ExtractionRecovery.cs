using Factum.Agent.Common;
using Factum.Agent.Models;
using Microsoft.Extensions.Options;

namespace Factum.Agent.Services.Extraction;

/// <summary>
/// Al arrancar Tatana (extraccion-logica-android §5.3/§5.9): todo diario en <c>running</c> quedó de una corrida que
/// se cortó (apagado, crash). Pasa a <c>interrupted / agent_stopped</c>, cierra el manifiesto parcial y escribe el
/// ACTA parcial. Si quedó <c>connector_pending_uninstall</c>, lo deja armado para <c>OnDevicesPolled</c>.
/// </summary>
public sealed class ExtractionRecovery(IOptions<AgentOptions> opts, ILogger<ExtractionRecovery> log)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var journal = new ExtractionJournal(opts.Value.DataDirectory);
        foreach (var doc in journal.LoadAll())
        {
            if (doc.Snapshot.Status != ExtractionStatuses.Running) continue;
            try { await RecoverOneAsync(journal, doc, ct); }
            catch (Exception ex) { log.LogWarning(ex, "No se pudo recuperar la extracción {Id}", doc.Snapshot.Id); }
        }
    }

    private async Task RecoverOneAsync(ExtractionJournal journal, ExtractionJournalDoc doc, CancellationToken ct)
    {
        var s = doc.Snapshot;
        var now = DateTime.UtcNow;
        s.Status = ExtractionStatuses.Interrupted;
        s.Phase = ExtractionPhases.Done;
        s.FinishedAt = now;
        s.Interruption = new InterruptionSnapshot { ReasonCode = "agent_stopped", At = now };
        foreach (var c in s.Categories)
            if (c.Status is CategoryStatuses.Pending or CategoryStatuses.Running)
                c.Status = CategoryStatuses.NotExtracted;

        if (Directory.Exists(doc.FolderPath))
        {
            var acta = BuildActa(s, doc);
            try { await ExtractionActaWriter.WriteAsync(doc.FolderPath, acta, ct); } catch { /* best-effort */ }

            // Acta/LEEME al manifiesto parcial antes de cerrar.
            await using (var mw = new ExtractionManifestWriter(doc.WorkDir))
            {
                foreach (var name in new[] { "ACTA.txt", "ACTA.json", "LEEME.txt" })
                {
                    var abs = Path.Combine(doc.FolderPath, name);
                    if (!File.Exists(abs)) continue;
                    var sha = await FileHashing.Sha256Async(abs, ct);
                    await mw.AppendAsync(new ManifestEntry(name, "", "factum", "acta", ManifestTypes.Acta,
                        new FileInfo(abs).Length, null, sha, null, ManifestMatch.NotApplicable, false,
                        ManifestStates.Ok, null), ct);
                }
            }
            try
            {
                var (sha, entries) = await ExtractionManifestWriter.CloseAsync(doc.WorkDir, doc.FolderPath, ct);
                s.ManifestSha256 = sha;
                s.ManifestEntries = entries;
                var actaTxt = await SafeSha(Path.Combine(doc.FolderPath, "ACTA.txt"));
                var actaJson = await SafeSha(Path.Combine(doc.FolderPath, "ACTA.json"));
                var cierre = ExtractionActaWriter.BuildCierre(s.Id, "Interrumpida", DateTime.Now, Offset(), sha,
                    actaTxt, actaJson, entries);
                await File.WriteAllTextAsync(Path.Combine(doc.FolderPath, "CIERRE.txt"), cierre,
                    new System.Text.UTF8Encoding(true), ct);
            }
            catch (Exception ex) { log.LogWarning(ex, "Cierre parcial de {Id}", s.Id); }
        }

        await journal.SaveAsync(doc, ct);
        log.LogInformation("Extracción {Id} recuperada como interrumpida (agent_stopped)", s.Id);
    }

    private static ActaData BuildActa(ExtractionSnapshot s, ExtractionJournalDoc doc) => new(
        ExtractionId: s.Id, Status: ExtractionStatuses.Interrupted, StatusLabel: "Interrumpida",
        OperatorDni: s.Operator.Dni, OperatorName: s.Operator.Name,
        Hostname: Environment.MachineName, OsUser: Environment.UserName, Os: Environment.OSVersion.VersionString,
        AgentVersion: AgentVersion.Current, AgentMode: "installed", ClientVersion: null,
        Device: new ActaDevice(s.Serial, s.Manufacturer, s.Model, s.AndroidVersion),
        StartedAtPcUtc: s.StartedAt, FinishedAtPcUtc: s.FinishedAt, PcMinusServerMs: null, PcServerMarginMs: null,
        DestinationPath: s.FolderPath, FileSystem: "", FreeBytesAtStart: 0,
        Ficha:
        [
            new ActaFichaField("serial_number", string.IsNullOrEmpty(s.Serial) ? null : s.Serial, string.IsNullOrEmpty(s.Serial) ? "empty" : null),
            new ActaFichaField("manufacturer", string.IsNullOrEmpty(s.Manufacturer) ? null : s.Manufacturer, string.IsNullOrEmpty(s.Manufacturer) ? "empty" : null),
            new ActaFichaField("model", string.IsNullOrEmpty(s.Model) ? null : s.Model, string.IsNullOrEmpty(s.Model) ? "empty" : null),
            new ActaFichaField("os_version", string.IsNullOrEmpty(s.AndroidVersion) ? null : s.AndroidVersion, string.IsNullOrEmpty(s.AndroidVersion) ? "empty" : null),
        ],
        ClockStartText: null, ClockCloseText: null,
        Categories: s.Categories.Select(c => new ActaCategoryLine(c.Key, c.Key,
            c.Kind == "files" ? "adb pull -a" : "content query / pm list", c.Status, c.Count, c.FilesTotal,
            c.BytesTotal, c.Errors, c.HashMismatches, c.Reason ?? c.ReasonCode)).ToList(),
        // Recuperación: la corrida se cortó, así que el hash en el dispositivo NO se completó. FilesComputed=0 →
        // el ACTA dice "No se calculó" con el motivo correcto (interrumpido / desactivado / sin sha256sum). H1.
        Verification: new ActaVerification(
            Requested: s.VerifyOnDevice,
            CapabilityAvailable: s.DeviceSha256Available,
            Binary: null,
            FilesComputed: 0,
            Mismatches: 0,
            FilesNotComputed: s.Totals.FilesDone),
        Modifications: s.DeviceModifications.Select(m => new ActaModification(m.Kind, m.At, m.Detail)).ToList(),
        InterruptionReasonText: "agent_stopped", InterruptionAtUtc: s.FinishedAt, ManifestSha256: "");

    private static async Task<string> SafeSha(string path)
    { try { return await FileHashing.Sha256Async(path); } catch { return ""; } }

    private static string Offset()
    {
        var off = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
        return (off < TimeSpan.Zero ? "-" : "+") + off.ToString(@"hh\:mm");
    }
}
