using MongoDB.Bson.Serialization.Attributes;

namespace Factum.Backend.Models;

/// <summary>
/// sellado-tiempo §4.1 (D-T2): un sello de tiempo RFC 3161 por objeto sellado — el par (caso, tipo, nombre,
/// hash) —, en la colección <c>case_timestamps</c>. Es la fuente de verdad del sello y, a la vez, la cola de
/// reintentos (<c>pending</c> con <see cref="NextAttemptAt"/>). Nunca guarda contenido de la evidencia: solo
/// el hash, los datos legibles del sello y el <c>.tsr</c> (la respuesta DER completa de la TSA).
/// [BsonIgnoreExtraElements] y opcionales con [BsonIgnoreIfNull]: tolera documentos de otra versión.
/// No se serializa directo a JSON: la API sale por <c>TimestampDto</c>.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class CaseTimestamp
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CaseId { get; set; } = "";
    /// <summary><see cref="TimestampTargets"/>.</summary>
    public string Target { get; set; } = "";
    /// <summary>Evidencia, <c>zip_filename</c> o <c>pdf_filename</c> (el DOCX).</summary>
    public string Filename { get; set; } = "";
    /// <summary>64 hex en minúscula: el hash sellado.</summary>
    public string Sha256 { get; set; } = "";
    /// <summary><see cref="TimestampStatuses"/>.</summary>
    public string Status { get; set; } = TimestampStatuses.Pending;
    /// <summary>Pedidos completos hechos (cada uno recorre la lista de URLs).</summary>
    public int Attempts { get; set; }
    /// <summary>UTC, reloj del servidor (<c>requested_at</c>).</summary>
    public DateTime CreatedAt { get; set; }
    /// <summary>Solo en <c>pending</c>.</summary>
    [BsonIgnoreIfNull] public DateTime? NextAttemptAt { get; set; }
    /// <summary>Lease del que lo está procesando (inline o la cola).</summary>
    [BsonIgnoreIfNull] public DateTime? LeaseUntil { get; set; }
    [BsonIgnoreIfNull] public string? LeaseId { get; set; }
    /// <summary>≤ 300 caracteres, sin userinfo de URLs.</summary>
    [BsonIgnoreIfNull] public string? LastError { get; set; }
    /// <summary><see cref="TimestampErrorKinds"/>.</summary>
    [BsonIgnoreIfNull] public string? LastErrorKind { get; set; }

    // ── solo en granted ──
    /// <summary>UTC, reloj del servidor al obtenerlo.</summary>
    [BsonIgnoreIfNull] public DateTime? GrantedAt { get; set; }
    /// <summary>URL que respondió, SIN userinfo.</summary>
    [BsonIgnoreIfNull] public string? TsaUrl { get; set; }
    /// <summary>Nombre legible (KnownAuthorities).</summary>
    [BsonIgnoreIfNull] public string? Authority { get; set; }
    /// <summary>Subject completo del certificado firmante.</summary>
    [BsonIgnoreIfNull] public string? SignerSubject { get; set; }
    /// <summary>UTC, del TSTInfo: la fecha certificada.</summary>
    [BsonIgnoreIfNull] public DateTime? GenTime { get; set; }
    /// <summary>Hex MAYÚSCULA, sin bytes 0x00 a la izquierda.</summary>
    [BsonIgnoreIfNull] public string? SerialNumber { get; set; }
    [BsonIgnoreIfNull] public string? PolicyOid { get; set; }
    /// <summary>TimeStampResp DER completo (lo que es un .tsr).</summary>
    [BsonIgnoreIfNull] public byte[]? Tsr { get; set; }

    // ── solo Target = zip ──
    /// <summary>true si el DOCX incluyó este sello.</summary>
    [BsonIgnoreIfNull] public bool? InReport { get; set; }
}

/// <summary>Valores literales de <see cref="CaseTimestamp.Target"/> (idénticos en Mongo y en el JSON).</summary>
public static class TimestampTargets
{
    public const string Evidence = "evidence", Zip = "zip", Report = "report";
}

/// <summary>Valores literales de <see cref="CaseTimestamp.Status"/>.</summary>
public static class TimestampStatuses
{
    public const string Pending = "pending", Granted = "granted", Failed = "failed";
}

/// <summary>Valores literales de <see cref="CaseTimestamp.LastErrorKind"/> (sellado-tiempo §5.2/§5.3).</summary>
public static class TimestampErrorKinds
{
    public const string Unreachable = "unreachable";
    public const string HttpError = "http_error";
    public const string Rejected = "rejected";
    public const string InvalidResponse = "invalid_response";
}
