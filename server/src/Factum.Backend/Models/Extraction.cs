using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;

namespace Factum.Backend.Models;

/// <summary>
/// extraccion-logica-android §6.1. Entidad de una extracción lógica (solo metadatos y el hash del manifiesto:
/// ningún byte del contenido extraído llega al servidor). Colección nueva <c>extractions</c>. El <c>Id</c> lo genera
/// Tatana (GUID "D") y el backend lo usa como <c>_id</c> (upsert idempotente, D-T1).
/// [BsonIgnoreExtraElements] + opcionales [BsonIgnoreIfNull]: tolera documentos de otra versión.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class Extraction
{
    [BsonId] public string Id { get; set; } = "";          // GUID "D" en minúscula, lo genera Tatana
    public int SchemaVersion { get; set; } = 1;
    public User Officer { get; set; } = new();              // dueño (del JWT), como Case.Officer
    public string Status { get; set; } = ExtractionStatuses.Running;
    public string Hostname { get; set; } = "";
    public string OsUser { get; set; } = "";
    public string AgentVersion { get; set; } = "";
    [BsonIgnoreIfNull] public AgentMode? AgentMode { get; set; }
    public DateTime StartedAtPc { get; set; }               // UTC, reloj de la PC (Tatana)
    [BsonIgnoreIfNull] public DateTime? FinishedAtPc { get; set; }
    public DateTime CreatedAt { get; set; }                 // UTC servidor, primer PUT
    public DateTime UpdatedAt { get; set; }                 // UTC servidor, último PUT
    [BsonIgnoreIfNull] public DateTime? FinalRegisteredAt { get; set; } // UTC servidor, primer PUT terminal
    [BsonIgnoreIfNull] public long? PcMinusServerMs { get; set; }       // sondeo NTP de la web
    [BsonIgnoreIfNull] public long? PcServerMarginMs { get; set; }
    public ExtractionDevice Device { get; set; } = new();
    [BsonIgnoreIfNull] public DeviceDetails? DeviceDetails { get; set; } // el MISMO modelo de #79
    public List<ExtractionCategory> Categories { get; set; } = [];
    public int CustomPathsCount { get; set; }               // nunca los nombres de carpetas
    public bool VerifyOnDevice { get; set; }
    public bool DeviceSha256Available { get; set; }
    public ExtractionTotals Totals { get; set; } = new();
    public string DestinationPath { get; set; } = "";       // texto informativo (D6)
    public string FolderName { get; set; } = "";
    [BsonIgnoreIfNull] public string? ManifestSha256 { get; set; }      // 64 hex minúscula; inmutable una vez puesto
    [BsonIgnoreIfNull] public int? ManifestEntries { get; set; }
    public List<DeviceModification> DeviceModifications { get; set; } = []; // append-only
    [BsonIgnoreIfNull] public ExtractionInterruption? Interruption { get; set; }
    [BsonIgnoreIfNull] public string? CaseId { get; set; }  // 0 o 1 caso (D6a)
    [BsonIgnoreIfNull] public DateTime? LinkedAt { get; set; }
}

[BsonIgnoreExtraElements]
public sealed class ExtractionDevice
{
    public string Serial { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public string Platform { get; set; } = "android";
    [BsonIgnoreIfNull] public string? AndroidVersion { get; set; }
    [BsonIgnoreIfNull] public int? SdkInt { get; set; }
}

[BsonIgnoreExtraElements]
public sealed class ExtractionCategory
{
    public string Key { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Status { get; set; } = "";
    [BsonIgnoreIfNull] public long? Count { get; set; }
    [BsonIgnoreIfNull] public int? Files { get; set; }
    [BsonIgnoreIfNull] public long? Bytes { get; set; }
    [BsonIgnoreIfNull] public int? Errors { get; set; }
    [BsonIgnoreIfNull] public int? HashMismatches { get; set; }
    [BsonIgnoreIfNull] public string? ReasonCode { get; set; }
    public string Method { get; set; } = "";
}

[BsonIgnoreExtraElements]
public sealed class ExtractionTotals
{
    public int Files { get; set; }
    public long Bytes { get; set; }
    public int Errors { get; set; }
    public int HashMismatches { get; set; }
}

[BsonIgnoreExtraElements]
public sealed class DeviceModification
{
    public string Kind { get; set; } = "";
    public DateTime At { get; set; }
    [BsonIgnoreIfNull] public string? Detail { get; set; }
    public bool AfterClose { get; set; }
}

[BsonIgnoreExtraElements]
public sealed class ExtractionInterruption
{
    public string ReasonCode { get; set; } = "";
    [BsonIgnoreIfNull] public string? Message { get; set; }
    public DateTime At { get; set; }
}

/// <summary>Valores literales de <see cref="Extraction.Status"/> (snake_case, idénticos en Mongo y en el JSON).</summary>
public static class ExtractionStatuses
{
    public const string Running = "running";
    public const string Completed = "completed";
    public const string CompletedWithErrors = "completed_with_errors";
    public const string Interrupted = "interrupted";
    public const string Cancelled = "cancelled";

    public static readonly string[] All = [Running, Completed, CompletedWithErrors, Interrupted, Cancelled];
    public static readonly string[] Terminal = [Completed, CompletedWithErrors, Interrupted, Cancelled];
    public static bool IsTerminal(string status) => Array.IndexOf(Terminal, status) >= 0;
    public static bool IsValid(string status) => Array.IndexOf(All, status) >= 0;
}

/// <summary>Valores literales de <see cref="DeviceModification.Kind"/>.</summary>
public static class DeviceModificationKinds
{
    public const string ConnectorInstalled = "connector_installed";
    public const string ConnectorUninstalled = "connector_uninstalled";
    public const string StayAwakeSet = "stay_awake_set";
    public const string StayAwakeRestored = "stay_awake_restored";

    public static readonly string[] All = [ConnectorInstalled, ConnectorUninstalled, StayAwakeSet, StayAwakeRestored];
    public static bool IsValid(string kind) => Array.IndexOf(All, kind) >= 0;
}

/// <summary>Claves de categoría válidas (§4.3) y su kind.</summary>
public static class ExtractionCategoryKeys
{
    public static readonly IReadOnlyDictionary<string, string> KindByKey = new Dictionary<string, string>
    {
        ["contacts"] = "structured",
        ["sms_mms"] = "structured",
        ["call_log"] = "structured",
        ["calendar"] = "structured",
        ["apps"] = "structured",
        ["media"] = "files",
        ["downloads"] = "files",
        ["documents"] = "files",
        ["audio"] = "files",
        ["whatsapp_media"] = "files",
        ["all_storage"] = "files",
        ["custom"] = "files",
    };

    public static bool IsValid(string key) => KindByKey.ContainsKey(key);
}

/// <summary>Estados por categoría (§4.3).</summary>
public static class ExtractionCategoryStatuses
{
    public const string Pending = "pending", Running = "running", Done = "done",
        DoneWithErrors = "done_with_errors", NotExtracted = "not_extracted", Cancelled = "cancelled";

    public static readonly string[] All = [Pending, Running, Done, DoneWithErrors, NotExtracted, Cancelled];
    public static bool IsValid(string s) => Array.IndexOf(All, s) >= 0;
}
