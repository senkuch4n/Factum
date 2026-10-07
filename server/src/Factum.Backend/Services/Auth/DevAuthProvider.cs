using Factum.Backend.Common;
using Factum.Backend.Models;

namespace Factum.Backend.Services.Auth;

public sealed class DevAuthProvider : IAuthProvider
{
    public string Mode => AuthModes.Dev;

    public Task<Result<AuthenticatedUser>> AuthenticateAsync(string dni, string? username, string password,
        CancellationToken ct = default)
    {
        // username dejó de ser [Required] en el DTO (en local no se manda): se valida acá.
        if (string.IsNullOrEmpty(username))
            return Task.FromResult(Result.Fail<AuthenticatedUser>("Usuario requerido"));

        if (dni.Length is < 7 or > 8)
            return Task.FromResult(Result.Fail<AuthenticatedUser>("DNI inválido: debe tener 7 u 8 dígitos"));

        if (string.IsNullOrEmpty(password))
            return Task.FromResult(Result.Fail<AuthenticatedUser>("Contraseña requerida"));

        var user = new User
        {
            Dni = dni,
            Name = FormatName(username),
            Sigla = "-"
        };
        return Task.FromResult(Result.Ok(new AuthenticatedUser(user, UserRoles.Cliente, false, null)));
    }

    // Convención de prueba: username = "nombre.apellido".
    private static string FormatName(string username)
    {
        var partes = username.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();
        return partes.Length >= 2 ? $"{Cap(partes[0])} {Cap(partes[1])}" : Cap(username);
    }
}
