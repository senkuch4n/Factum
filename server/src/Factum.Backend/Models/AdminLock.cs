using MongoDB.Bson.Serialization.Attributes;

namespace Factum.Backend.Models;

/// <summary>
/// Mutex con lease (colección <c>user_admin_locks</c>, abm-clientes §4.3) para serializar la
/// suspensión de superadmins sin transacciones. No guarda datos de negocio.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class AdminLock
{
    /// <summary>Clave del lock, p. ej. <c>"superadmin_status"</c>.</summary>
    [BsonId] public string Id { get; set; } = "";
    /// <summary>GUID de la operación que lo tiene.</summary>
    public string Owner { get; set; } = "";
    /// <summary>Lease: now + 10 s. Vencido, la próxima adquisición lo pisa.</summary>
    public DateTime ExpiresAt { get; set; }
}
