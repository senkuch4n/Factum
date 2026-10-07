using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Factum.Backend.Services.Auth;

/// <summary>Hash de contraseñas de las cuentas locales (usuarios-locales §4.2).</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    /// <summary><c>false</c> si no coincide o si <paramref name="encoded"/> está mal formado (nunca tira).</summary>
    bool Verify(string password, string encoded);
    /// <summary><c>true</c> si el hash usa menos iteraciones que las actuales.</summary>
    bool NeedsRehash(string encoded);
}

/// <summary>
/// PBKDF2-HMAC-SHA256 de la BCL. Formato versionado:
/// <c>pbkdf2-sha256$&lt;iteraciones&gt;$&lt;sal base64&gt;$&lt;hash base64&gt;</c>. Sal de 16 bytes,
/// clave de 32 bytes, 600 000 iteraciones (OWASP 2023). Comparación en tiempo constante.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    public const string Prefix = "pbkdf2-sha256";
    public const int DefaultIterations = 600_000;
    private const int SaltBytes = 16;
    private const int KeyBytes = 32;
    // Tope defensivo: un hash corrupto con iteraciones gigantes no puede colgar un request.
    private const int MaxIterations = 10_000_000;

    private readonly int _iterations;

    public Pbkdf2PasswordHasher() : this(DefaultIterations) { }

    /// <summary>Los tests usan pocas iteraciones (1 000) para no tardar.</summary>
    internal Pbkdf2PasswordHasher(int iterations)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);
        _iterations = iterations;
    }

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var key = Derive(password, salt, _iterations, KeyBytes);
        return string.Join('$', Prefix, _iterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt), Convert.ToBase64String(key));
    }

    public bool Verify(string password, string encoded)
    {
        if (password is null || !TryParse(encoded, out var iterations, out var salt, out var expected))
            return false;
        var actual = Derive(password, salt, iterations, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public bool NeedsRehash(string encoded) =>
        !TryParse(encoded, out var iterations, out _, out _) || iterations < _iterations;

    private static byte[] Derive(string password, byte[] salt, int iterations, int length) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, length);

    private static bool TryParse(string? encoded, out int iterations, out byte[] salt, out byte[] hash)
    {
        iterations = 0;
        salt = [];
        hash = [];
        if (string.IsNullOrEmpty(encoded)) return false;
        var parts = encoded.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix) return false;
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out iterations) ||
            iterations < 1 || iterations > MaxIterations)
            return false;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            hash = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }
        return salt.Length > 0 && hash.Length > 0;
    }
}
