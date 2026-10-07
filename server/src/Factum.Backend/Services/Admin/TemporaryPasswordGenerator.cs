using System.Security.Cryptography;

namespace Factum.Backend.Services.Admin;

/// <summary>Genera la contraseña temporal del alta y del reset (abm-clientes §6.7, D7).</summary>
public interface ITemporaryPasswordGenerator
{
    string Generate(int minLength);
}

/// <summary>
/// Grupos de 4 caracteres de un alfabeto sin ambiguos, unidos por <c>-</c> (<c>k7mq-x2rt-9vhp</c>).
/// Usa <see cref="RandomNumberGenerator"/> (uniforme, sin sesgo de módulo); nunca <c>System.Random</c>.
/// El valor nunca entra en logs, excepciones ni <c>ToString</c>.
/// </summary>
public sealed class TemporaryPasswordGenerator : ITemporaryPasswordGenerator
{
    /// <summary>Sin 0/o, 1/l/i (y por eso todo en minúscula). 23 letras + 8 dígitos = 31.</summary>
    public const string Alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
    public const int GroupSize = 4;
    public const int MinGroups = 3;

    /// <summary><c>n = max(3, ceil((minLength + 1) / 5))</c>; largo total <c>5n − 1</c> ≥ <paramref name="minLength"/>.</summary>
    public static int GroupsFor(int minLength) => Math.Max(MinGroups, (int)Math.Ceiling((minLength + 1) / 5.0));

    public string Generate(int minLength)
    {
        var groups = GroupsFor(minLength);
        var chars = RandomNumberGenerator.GetItems<char>(Alphabet, groups * GroupSize);
        var result = new char[groups * (GroupSize + 1) - 1];
        var pos = 0;
        for (var g = 0; g < groups; g++)
        {
            if (g > 0) result[pos++] = '-';
            for (var i = 0; i < GroupSize; i++) result[pos++] = chars[g * GroupSize + i];
        }
        Array.Clear(chars);
        return new string(result);
    }
}
