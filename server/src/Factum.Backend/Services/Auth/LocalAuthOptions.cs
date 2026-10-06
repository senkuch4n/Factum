using System.Text.RegularExpressions;

namespace Factum.Backend.Services.Auth;

/// <summary>Formato de DNI de una cuenta local: 7 u 8 dígitos ASCII (D3, <c>^\d{7,8}$</c>).</summary>
public static partial class DniFormat
{
    [GeneratedRegex(@"^[0-9]{7,8}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool IsValid(string? dni) => dni is not null && Pattern().IsMatch(dni);
}

/// <summary>Superadmin inicial de <c>Auth:Local:BootstrapSuperadmins[i]</c> (D9).</summary>
public sealed record BootstrapSuperadmin(string Dni, string Name, string TemporaryPassword)
{
    // La contraseña temporal nunca sale en un ToString (logs, excepciones, depurador).
    public override string ToString() => $"BootstrapSuperadmin {{ Dni = {Dni}, Name = {Name} }}";
}

/// <summary>Reset de emergencia de <c>Auth:Local:ResetSuperadmin</c> (D10).</summary>
public sealed record SuperadminReset(string Dni, string TemporaryPassword)
{
    public override string ToString() => $"SuperadminReset {{ Dni = {Dni} }}";
}

/// <summary>
/// Configuración ya resuelta y validada de <c>Auth:Local:*</c> (usuarios-locales §4.3). La arma
/// <see cref="AuthSettingsResolver"/>; solo existe con <c>Auth:Mode=local</c>.
/// </summary>
public sealed class LocalAuthOptions
{
    public const int PasswordMaxLength = 128;
    public const int DefaultPasswordMinLength = 10;
    public int PasswordMinLength { get; init; } = DefaultPasswordMinLength;
    public int MaxFailedAttempts { get; init; } = 5;
    public int LockoutMinutes { get; init; } = 15;
    public IReadOnlyList<BootstrapSuperadmin> BootstrapSuperadmins { get; init; } = [];
    public SuperadminReset? ResetSuperadmin { get; init; }
}
