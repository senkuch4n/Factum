using System.Text;
using System.Text.Json.Nodes;
using Factum.Agent.Common;

namespace Factum.Agent.Services.Extraction;

/// <summary>Resultado de una categoría estructurada.</summary>
public sealed record StructuredResult(string Status, long? Count, string? ReasonCode, List<ManifestEntry> Entries,
    string Method);

/// <summary>
/// Datos estructurados por <c>content query</c> / <c>pm list</c> (extraccion-logica-android §5.3). Guarda el crudo
/// (evidencia primaria) y el normalizado (derivado), y clasifica permisos/URIs. El crudo se escribe con
/// <see cref="ProcessRunner.RunToFileAsync"/> directo al archivo. Camino REAL (requiere adb + dispositivo).
/// </summary>
public sealed class StructuredExtractor(string adbPath, string serial, string folder,
    ExtractionCommandLog? cmdLog = null)
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>URIs/comando por categoría (§5.3.1). La URI "principal" (para el count) va primera.</summary>
    public static IReadOnlyList<(string File, string Uri)> UrisFor(string key) => key switch
    {
        "contacts" =>
        [
            ("contacts_contacts.txt", "content://com.android.contacts/contacts"),
            ("contacts_raw_contacts.txt", "content://com.android.contacts/raw_contacts"),
            ("contacts_data.txt", "content://com.android.contacts/data"),
        ],
        "sms_mms" =>
        [
            ("sms.txt", "content://sms"),
            ("mms.txt", "content://mms"),
            ("mms_part.txt", "content://mms/part"),
        ],
        "calendar" =>
        [
            ("calendar_calendars.txt", "content://com.android.calendar/calendars"),
            ("calendar_events.txt", "content://com.android.calendar/events"),
            ("calendar_attendees.txt", "content://com.android.calendar/attendees"),
            ("calendar_reminders.txt", "content://com.android.calendar/reminders"),
        ],
        "call_log" => [("call_log_calls.txt", "content://app.factum.connector.calllog/calls")],
        _ => [],
    };

    /// <summary>Corre una categoría estructurada (menos <c>apps</c>, que usa <see cref="RunAppsAsync"/>).</summary>
    public async Task<StructuredResult> RunCategoryAsync(string key, CancellationToken ct)
    {
        var uris = UrisFor(key);
        var entries = new List<ManifestEntry>();
        var crudoDir = Path.Combine(folder, "datos", "crudo");
        var normDir = Path.Combine(folder, "datos", "normalizado");
        Directory.CreateDirectory(crudoDir);
        Directory.CreateDirectory(normDir);

        int okUris = 0, failedUris = 0;
        string? reason = null;
        long? primaryCount = null;

        foreach (var (file, uri) in uris)
        {
            var crudoPath = Path.Combine(crudoDir, file);
            var args = new[] { "-s", serial, "shell", "-T", "content", "query", "--uri", uri };
            var start = DateTime.UtcNow;
            var (exit, stderr) = await ProcessRunner.RunToFileAsync(adbPath, args, crudoPath,
                TimeSpan.FromMinutes(10), ct);
            cmdLog?.Log(adbPath, args, exit, stderr, start, DateTime.UtcNow);

            var crudoRel = $"datos/crudo/{file}";
            if (exit != 0 && stderr.Contains("SecurityException", StringComparison.Ordinal))
            { failedUris++; reason = ConnectorReasonCodes.Unavailable == reason ? reason : "permission_denied"; }
            else if (stderr.Contains("Unknown URI", StringComparison.Ordinal) ||
                     stderr.Contains("Could not find provider", StringComparison.Ordinal))
            { failedUris++; reason ??= "provider_unavailable"; }
            else okUris++;

            entries.Add(await Entry(crudoRel, uri, key, ManifestTypes.Evidence, ct));

            // Normalizado (derivado) best-effort.
            var text = File.Exists(crudoPath) ? await File.ReadAllTextAsync(crudoPath, ct) : "";
            var parsed = ContentQueryParser.Parse(text);
            var normName = Path.GetFileNameWithoutExtension(file) + ".json";
            await WriteNormalizedAsync(Path.Combine(normDir, normName), crudoRel, uri, parsed, ct);
            entries.Add(await Entry($"datos/normalizado/{normName}", uri, key, ManifestTypes.Derived, ct));
            if (primaryCount is null && okUris > 0) primaryCount = parsed.RowCount;
        }

        var status = okUris == 0 ? CategoryStatuses.NotExtracted
            : failedUris > 0 ? CategoryStatuses.DoneWithErrors
            : CategoryStatuses.Done;
        var method = "content query " + string.Join(", ", uris.Select(u => u.Uri));
        return new StructuredResult(status, primaryCount, okUris == 0 ? reason ?? "error" : null, entries, method);
    }

    /// <summary>Apps instaladas: <c>pm list packages</c> (§5.3.1).</summary>
    public async Task<StructuredResult> RunAppsAsync(CancellationToken ct)
    {
        var crudoDir = Path.Combine(folder, "datos", "crudo");
        Directory.CreateDirectory(crudoDir);
        var entries = new List<ManifestEntry>();
        var cmds = new (string File, string[] Args)[]
        {
            ("apps_pm_list_packages.txt", ["-s", serial, "shell", "-T", "pm", "list", "packages", "-f", "-i", "-U", "--show-versioncode"]),
            ("apps_pm_list_packages_u.txt", ["-s", serial, "shell", "-T", "pm", "list", "packages", "-u"]),
        };
        int lines = 0;
        foreach (var (file, args) in cmds)
        {
            var path = Path.Combine(crudoDir, file);
            var start = DateTime.UtcNow;
            var (exit, stderr) = await ProcessRunner.RunToFileAsync(adbPath, args, path, TimeSpan.FromMinutes(2), ct);
            cmdLog?.Log(adbPath, args, exit, stderr, start, DateTime.UtcNow);
            if (file == "apps_pm_list_packages.txt" && File.Exists(path))
                lines = (await File.ReadAllLinesAsync(path, ct)).Count(l => l.StartsWith("package:", StringComparison.Ordinal));
            entries.Add(await Entry($"datos/crudo/{file}", "pm list packages", "apps", ManifestTypes.Evidence, ct));
        }
        return new StructuredResult(CategoryStatuses.Done, lines, null, entries, "pm list packages -f -i -U --show-versioncode");
    }

    private static async Task WriteNormalizedAsync(string path, string crudoRel, string uri, ParsedQuery parsed,
        CancellationToken ct)
    {
        var rows = new JsonArray();
        foreach (var r in parsed.Rows)
        {
            var o = new JsonObject();
            if (r.ParseWarning) { o["_raw"] = r.Raw; o["_parse_warning"] = true; }
            else foreach (var (k, v) in r.Values) o[k] = v;
            rows.Add(o);
        }
        var norm = new JsonObject
        {
            ["schema"] = "factum-extraccion-normalizado/1",
            ["derived_from"] = new JsonArray(crudoRel),
            ["source_uri"] = uri,
            ["columns"] = new JsonArray(parsed.Columns.Select(c => (JsonNode?)c).ToArray()),
            ["rows"] = rows,
            ["row_count"] = parsed.RowCount,
            ["parse_warnings"] = parsed.ParseWarnings,
        };
        await File.WriteAllTextAsync(path, norm.ToJsonString(), Utf8NoBom, ct);
    }

    private async Task<ManifestEntry> Entry(string rel, string origin, string category, string type,
        CancellationToken ct)
    {
        var abs = Path.Combine(folder, rel.Replace('/', Path.DirectorySeparatorChar));
        var size = File.Exists(abs) ? new FileInfo(abs).Length : 0;
        var sha = File.Exists(abs) ? await FileHashing.Sha256Async(abs, ct) : null;
        return new ManifestEntry(rel, "", origin, category, type, size, null, sha, null,
            ManifestMatch.NotApplicable, false, ManifestStates.Ok, null);
    }
}
