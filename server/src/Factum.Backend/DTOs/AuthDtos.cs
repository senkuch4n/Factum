using System.ComponentModel.DataAnnotations;

namespace Factum.Backend.DTOs;

/// <summary>
/// <c>username</c> es opcional: en local no se manda; en dev/external, si falta, el proveedor
/// responde 401 "Usuario requerido" (usuarios-locales §3.4, §5.2).
/// </summary>
public sealed record LoginRequest(
    [Required] string Dni,
    [Required] string Password,
    string? Username = null
);

public sealed record LoginResponse(string Token, UserDto User);

/// <summary>JSON: <c>dni, name, sigla, role, must_change_password</c> (contrato §8.1).</summary>
public sealed record UserDto(string Dni, string Name, string Sigla, string Role, bool MustChangePassword);

/// <summary>JSON: <c>current_password, new_password, new_password_confirmation</c> (§5.3).</summary>
public sealed record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required] string NewPassword,
    [Required] string NewPasswordConfirmation
);
