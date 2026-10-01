using System.ComponentModel.DataAnnotations;

namespace Factum.Backend.DTOs;

public sealed record LoginRequest(
    [Required] string Dni,
    [Required] string Username,
    [Required] string Password
);

public sealed record LoginResponse(string Token, UserDto User);

public sealed record UserDto(string Dni, string Name, string Sigla);
