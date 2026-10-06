using MongoDB.Bson.Serialization.Attributes;

namespace Factum.Backend.Models;

/// <summary>Acciones del panel de administración de cuentas (abm-clientes §4.2, contrato §8.1).</summary>
public static class UserAdminActions
{
    public const string Create = "create";
    public const string Update = "update";
    public const string Suspend = "suspend";
    public const string Reactivate = "reactivate";
    public const string ResetPassword = "reset_password";
    public const string Unlock = "unlock";
}

/// <summary>
/// Auditoría de una acción administrativa exitosa (colección <c>user_admin_events</c>, D5/D6). De solo
/// inserción: ni la API ni el repositorio tienen update ni delete. Nunca lleva contraseñas ni hashes.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class UserAdminEvent
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>UTC, truncado a ms.</summary>
    public DateTime At { get; set; }
    public string ActorDni { get; set; } = "";
    /// <summary>Copia: el actor puede cambiar de nombre.</summary>
    public string ActorName { get; set; } = "";
    public string TargetUserId { get; set; } = "";
    public string TargetDni { get; set; } = "";
    /// <summary>Nombre de la cuenta al momento de la acción.</summary>
    public string TargetName { get; set; } = "";
    /// <summary><see cref="UserAdminActions"/>.</summary>
    public string Action { get; set; } = "";
    /// <summary>Vacía salvo <c>create</c>/<c>update</c>.</summary>
    public List<UserAdminChange> Changes { get; set; } = [];
    /// <summary>Solo <c>suspend</c> (D10).</summary>
    public string? Reason { get; set; }
    public string? Ip { get; set; }
}

public sealed class UserAdminChange
{
    /// <summary>Nombre del campo EN EL JSON: <c>name</c>, <c>contact_phone</c>, …</summary>
    public string Field { get; set; } = "";
    public string? From { get; set; }
    public string? To { get; set; }
}
