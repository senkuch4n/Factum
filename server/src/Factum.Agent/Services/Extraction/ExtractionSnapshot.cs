using System.Text.Json.Serialization;
using Factum.Agent.Models;

namespace Factum.Agent.Services.Extraction;

// extraccion-logica-android §8.2.6. Snapshot que viaja en las respuestas y por WebSocket. Los opcionales llevan
// [JsonIgnore(WhenWritingNull)] a nivel propiedad para que un null NO viaje, aunque el hub serialice sin esa opción
// global. Se construye fresco en cada broadcast/respuesta (sin compartir estado mutable entre hilos).

public static class ExtractionPhases
{
    public const string Preparing = "preparing", Structured = "structured", Inventory = "inventory",
        Copying = "copying", Hashing = "hashing", VerifyingDevice = "verifying_device", Closing = "closing",
        Done = "done";
}

public static class ExtractionStatuses
{
    public const string Running = "running", Completed = "completed", CompletedWithErrors = "completed_with_errors",
        Interrupted = "interrupted", Cancelled = "cancelled";

    public static readonly string[] Terminal = [Completed, CompletedWithErrors, Interrupted, Cancelled];
    public static bool IsTerminal(string s) => Array.IndexOf(Terminal, s) >= 0;
}

public static class CategoryStatuses
{
    public const string Pending = "pending", Running = "running", Done = "done",
        DoneWithErrors = "done_with_errors", NotExtracted = "not_extracted", Cancelled = "cancelled";
}

public static class DeviceModificationKinds
{
    public const string ConnectorInstalled = "connector_installed", ConnectorUninstalled = "connector_uninstalled",
        StayAwakeSet = "stay_awake_set", StayAwakeRestored = "stay_awake_restored";
}

public sealed class ExtractionOperator
{
    public string Dni { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class CategorySnapshot
{
    public string Key { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Status { get; set; } = CategoryStatuses.Pending;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? Count { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? FilesTotal { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? FilesDone { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? BytesTotal { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? BytesDone { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Errors { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? HashMismatches { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ReasonCode { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Reason { get; set; }
}

public sealed class TotalsSnapshot
{
    public int FilesTotal { get; set; }
    public int FilesDone { get; set; }
    public long BytesTotal { get; set; }
    public long BytesDone { get; set; }
    public int Errors { get; set; }
    public int HashMismatches { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? BytesPerSecond { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? EtaSeconds { get; set; }
}

public sealed class ModificationSnapshot
{
    public string Kind { get; set; } = "";
    public DateTime At { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Detail { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? AfterClose { get; set; }
}

public sealed class InterruptionSnapshot
{
    public string ReasonCode { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Message { get; set; }
    public DateTime At { get; set; }
}

public sealed class ExtractionSnapshot
{
    public string Id { get; set; } = "";
    public string Status { get; set; } = ExtractionStatuses.Running;
    public string Phase { get; set; } = ExtractionPhases.Preparing;
    public string Serial { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? AndroidVersion { get; set; }
    public ExtractionOperator Operator { get; set; } = new();
    public string DestinationPath { get; set; } = "";
    public string FolderName { get; set; } = "";
    public string FolderPath { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool VerifyOnDevice { get; set; }
    public bool DeviceSha256Available { get; set; }
    public List<CategorySnapshot> Categories { get; set; } = [];
    public TotalsSnapshot Totals { get; set; } = new();
    public List<ModificationSnapshot> DeviceModifications { get; set; } = [];
    public bool Registered { get; set; }
    public bool SealsWritten { get; set; }

    // Solo en el terminal.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTime? FinishedAt { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public InterruptionSnapshot? Interruption { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ManifestSha256 { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? ManifestEntries { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AgentExtractionRecord? Record { get; set; }
}

/// <summary>Fila del índice local (§8.2.5 <c>GET /extractions</c>).</summary>
public sealed class ExtractionListItem
{
    public string Id { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime StartedAt { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTime? FinishedAt { get; set; }
    public string FolderPath { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public bool Registered { get; set; }
    public bool SealsWritten { get; set; }
}
