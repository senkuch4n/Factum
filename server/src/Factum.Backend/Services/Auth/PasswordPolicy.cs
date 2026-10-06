namespace Factum.Backend.Services.Auth;

/// <summary>Error de una regla de contraseña: <c>code</c> y <c>field</c> del contrato (§8.2).</summary>
public sealed record PasswordRuleError(string Code, string Field, string Message);

/// <summary>
/// Política de la contraseña nueva (usuarios-locales §6.7, D4). Pura. Se mide con
/// <see cref="string.Length"/> (UTF-16, igual que <c>.length</c> en JS) y sin trim. Los textos son
/// los de §8.3 y tienen que coincidir con <c>client/src/lib/password-policy.ts</c>.
/// </summary>
public static class PasswordPolicy
{
    public const string FieldCurrent = "current_password";
    public const string FieldNew = "new_password";
    public const string FieldConfirmation = "new_password_confirmation";

    public const string CodeMismatch = "password_mismatch";
    public const string CodeTooShort = "password_too_short";
    public const string CodeTooLong = "password_too_long";
    public const string CodeSameAsCurrent = "password_same_as_current";
    public const string CodeContainsDni = "password_contains_dni";
    public const string CodeInvalidCurrent = "invalid_current_password";

    public const string MsgMismatch = "Las contraseñas nuevas no coinciden.";
    public const string MsgTooLong = "La contraseña nueva puede tener hasta 128 caracteres.";
    public const string MsgSameAsCurrent = "La contraseña nueva tiene que ser distinta de la actual.";
    public const string MsgContainsDni = "La contraseña nueva no puede contener tu DNI.";
    public const string MsgInvalidCurrent = "La contraseña actual no es correcta.";
    public static string MsgTooShort(int min) => $"La contraseña nueva tiene que tener al menos {min} caracteres.";

    /// <summary>Orden: mismatch → too_short → too_long → contains_dni. <c>null</c> = cumple.</summary>
    public static PasswordRuleError? ValidateNew(string newPassword, string confirmation, string dni, int minLength)
    {
        newPassword ??= string.Empty;
        confirmation ??= string.Empty;
        if (!string.Equals(newPassword, confirmation, StringComparison.Ordinal))
            return new PasswordRuleError(CodeMismatch, FieldConfirmation, MsgMismatch);
        if (newPassword.Length < minLength)
            return new PasswordRuleError(CodeTooShort, FieldNew, MsgTooShort(minLength));
        if (newPassword.Length > LocalAuthOptions.PasswordMaxLength)
            return new PasswordRuleError(CodeTooLong, FieldNew, MsgTooLong);
        if (!string.IsNullOrEmpty(dni) && newPassword.Contains(dni, StringComparison.Ordinal))
            return new PasswordRuleError(CodeContainsDni, FieldNew, MsgContainsDni);
        return null;
    }

    public static PasswordRuleError SameAsCurrent() => new(CodeSameAsCurrent, FieldNew, MsgSameAsCurrent);

    public static PasswordRuleError InvalidCurrent() => new(CodeInvalidCurrent, FieldCurrent, MsgInvalidCurrent);
}
