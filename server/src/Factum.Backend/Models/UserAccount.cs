using MongoDB.Bson.Serialization.Attributes;

namespace Factum.Backend.Models;

/// <summary>Roles de una cuenta propia de Factum (<c>Auth:Mode=local</c>).</summary>
public static class UserRoles
{
    public const string Superadmin = "superadmin";
    public const string Cliente = "cliente";
}

/// <summary>Estados de una cuenta propia de Factum.</summary>
public static class UserStatuses
{
    public const string Activo = "activo";
    public const string Suspendido = "suspendido";
}

/// <summary>
/// Cuenta propia de Factum (colección <c>users</c>, usuarios-locales §4.1). El DNI es la identidad y la
/// llave de los datos del usuario (casos, perfil, catálogos); <c>_id</c> es un GUID string. Nunca se
/// serializa a JSON: la API expone DTOs explícitos. <see cref="Models.User"/> no cambia porque
/// <c>Case.Officer</c> lo persiste.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class UserAccount
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>7-8 dígitos, único (índice <c>ux_users_dni</c>), no editable (D3).</summary>
    public string Dni { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Sigla { get; set; } = string.Empty;
    /// <summary><see cref="UserRoles"/>.</summary>
    public string Role { get; set; } = UserRoles.Cliente;
    /// <summary><see cref="UserStatuses"/>.</summary>
    public string Status { get; set; } = UserStatuses.Activo;
    /// <summary>Formato <c>pbkdf2-sha256$iter$sal$hash</c>. Nunca sale por la API ni al log.</summary>
    public string PasswordHash { get; set; } = string.Empty;
    public bool MustChangePassword { get; set; } = true;
    /// <summary>UTC, truncado a milisegundos. Lo compara el claim <c>pca</c> del JWT.</summary>
    public DateTime PasswordChangedAt { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    /// <summary>DNI del superadmin que la creó, o <c>"bootstrap"</c>.</summary>
    public string? CreatedBy { get; set; }
    public DateTime? SuspendedAt { get; set; }
    public string? SuspendedBy { get; set; }
    public DateTime? LastLoginAt { get; set; }
    /// <summary>Marca hasheada del último <c>Auth:Local:ResetSuperadmin</c> aplicado (§7.2).</summary>
    public string? LastEmergencyResetHash { get; set; }

    // ── abm-clientes §4.1 (D1, D10): faltan en los documentos previos → defaults de C# ──
    /// <summary>Teléfono de contacto (opcional, D1).</summary>
    public string ContactPhone { get; set; } = string.Empty;
    /// <summary>Email de contacto (opcional, D1). Factum no le manda mails.</summary>
    public string ContactEmail { get; set; } = string.Empty;
    /// <summary>Estudio, fuerza o razón social (opcional, ≤ 120).</summary>
    public string Organization { get; set; } = string.Empty;
    /// <summary>Notas internas de los superadmins (≤ 1000). Nunca sale en <c>UserDto</c> ni en <c>/me</c>.</summary>
    public string Notes { get; set; } = string.Empty;
    /// <summary>Motivo de la suspensión vigente (≤ 300, D10/T6). $unset al reactivar. El cliente no lo ve.</summary>
    public string? SuspensionReason { get; set; }
}
