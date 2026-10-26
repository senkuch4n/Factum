using System.Text.Json;
using Factum.Agent.Services.Session;

namespace Factum.Agent.Services.Extraction;

/// <summary>Connector pendiente de desinstalar (§5.5 paso 5/6).</summary>
public sealed record ConnectorPending(string Serial, string Package);

/// <summary>
/// Documento del diario de una extracción (extraccion-logica-android §5.1): el snapshot más lo que hace falta para
/// la recuperación y la limpieza del connector al reconectar.
/// </summary>
public sealed class ExtractionJournalDoc
{
    public ExtractionSnapshot Snapshot { get; set; } = new();
    public string FolderPath { get; set; } = "";
    public string WorkDir { get; set; } = "";
    public ConnectorPending? ConnectorPendingUninstall { get; set; }
    public StayAwakeState? StayAwake { get; set; }
    public bool RestorePending { get; set; }
}

/// <summary>
/// Diario JSON por extracción en <c>&lt;DataDirectory&gt;/extractions/&lt;id&gt;.json</c>, escrito con temp + rename
/// (extraccion-logica-android §5.1). Es lo que leen <c>GET /extractions</c> y la recuperación.
/// </summary>
public sealed class ExtractionJournal
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };

    public string Directory { get; }

    public ExtractionJournal(string dataDirectory)
    {
        Directory = Path.Combine(dataDirectory, "extractions");
        System.IO.Directory.CreateDirectory(Directory);
    }

    public string PathFor(string id) => Path.Combine(Directory, id + ".json");

    public async Task SaveAsync(ExtractionJournalDoc doc, CancellationToken ct = default)
    {
        var path = PathFor(doc.Snapshot.Id);
        var tmp = path + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(doc, Json), ct);
        File.Move(tmp, path, overwrite: true);
    }

    public ExtractionJournalDoc? Load(string path)
    {
        try { return JsonSerializer.Deserialize<ExtractionJournalDoc>(File.ReadAllText(path), Json); }
        catch { return null; }
    }

    public IEnumerable<ExtractionJournalDoc> LoadAll()
    {
        if (!System.IO.Directory.Exists(Directory)) yield break;
        foreach (var f in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            var doc = Load(f);
            if (doc is not null && !string.IsNullOrEmpty(doc.Snapshot.Id)) yield return doc;
        }
    }
}
