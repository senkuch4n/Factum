namespace Factum.Backend.DTOs;

// abm-clientes §7 / contrato §8.1. Se serializan con JsonNamingPolicy.SnakeCaseLower (Program.cs).
// Los requests NO llevan [Required]: son string? y el servicio valida (validation_failed + field).

/// <summary>Cuenta para el panel. NUNCA lleva PasswordHash ni LastEmergencyResetHash.</summary>
public sealed record AdminUserDto(
    string Id, string Dni, string Name, string Sigla, string Role, string Status,
    bool MustChangePassword, DateTime? LockedUntil, DateTime? LastLoginAt,
    DateTime CreatedAt, string? CreatedBy, string? CreatedByName, DateTime UpdatedAt,
    DateTime? SuspendedAt, string? SuspendedBy, string? SuspendedByName, string? SuspensionReason,
    string ContactPhone, string ContactEmail, string Organization, string Notes,
    long CaseCount);

public sealed record AdminUserListResponse(List<AdminUserDto> Users);
public sealed record AdminUserResponse(AdminUserDto User);

/// <summary>Respuesta de E3 (alta) y E7 (reset). La temporal sale solo acá, nunca en ToString.</summary>
public sealed record AdminUserWithPasswordResponse(AdminUserDto User, string TemporaryPassword)
{
    public override string ToString() => $"AdminUserWithPasswordResponse {{ User = {User.Id} }}";
}

public sealed record AdminUpdateUserResponse(AdminUserDto User, bool Changed);

/// <summary>E3. No tiene <c>role</c>: el alta es siempre <c>cliente</c> (D3, T9).</summary>
public sealed record AdminCreateUserRequest(
    string? Dni, string? Name, string? Sigla,
    string? ContactPhone, string? ContactEmail, string? Organization, string? Notes);

/// <summary>E4. Reemplaza los 6 campos editables; <c>expected_updated_at</c> es el token de D11.</summary>
public sealed record AdminUpdateUserRequest(
    string? Name, string? Sigla, string? ContactPhone, string? ContactEmail, string? Organization, string? Notes,
    DateTime? ExpectedUpdatedAt);

public sealed record AdminSuspendRequest(string? Reason);

public sealed record AdminUserChangeDto(string Field, string? From, string? To);

public sealed record AdminUserEventDto(
    string Id, DateTime At, string ActorDni, string ActorName, string Action,
    List<AdminUserChangeDto> Changes, string? Reason, string? Ip);

public sealed record AdminUserEventsResponse(List<AdminUserEventDto> Events, bool HasMore);
