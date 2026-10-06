using System.Text.RegularExpressions;
using Factum.Backend.Services.Auth;

namespace Factum.Backend.Services.Admin;

/// <summary>
/// Reglas de los campos de una cuenta (abm-clientes §6.4). Pura. Todas hacen trim (<c>null</c> → <c>""</c>)
/// y miden con <see cref="string.Length"/> (igual que <c>.length</c> en JS). Las mismas reglas y regexes
/// están en <c>client/src/lib/admin-accounts.ts</c>.
/// </summary>
public static partial class AccountFieldRules
{
    public const int NameMax = 120;
    public const int SiglaMax = 30;
    public const int PhoneMaxLength = 40;
    public const int PhoneMinDigits = 6;
    public const int PhoneMaxDigits = 20;
    public const int EmailMax = 254;
    public const int OrganizationMax = 120;
    public const int NotesMax = 1000;
    public const int ReasonMax = 300;

    [GeneratedRegex(@"^\+?[0-9][0-9 ()\-.]*$", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    /// <summary>Trim de los bordes; <c>null</c> → <c>""</c>. Los saltos de línea internos se conservan.</summary>
    public static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    public static FieldError? Dni(string? value) =>
        DniFormat.IsValid(Normalize(value)) ? null : new FieldError(AdminErrors.FieldDni, AdminErrors.MsgDni);

    public static FieldError? Name(string? value)
    {
        var v = Normalize(value);
        if (v.Length == 0) return new FieldError(AdminErrors.FieldName, AdminErrors.MsgNameRequired);
        return v.Length > NameMax ? new FieldError(AdminErrors.FieldName, AdminErrors.MsgNameTooLong) : null;
    }

    public static FieldError? Sigla(string? value) =>
        Normalize(value).Length > SiglaMax ? new FieldError(AdminErrors.FieldSigla, AdminErrors.MsgSigla) : null;

    public static FieldError? ContactPhone(string? value)
    {
        var v = Normalize(value);
        if (v.Length == 0) return null;
        var digits = v.Count(c => c is >= '0' and <= '9');
        var ok = v.Length <= PhoneMaxLength && PhonePattern().IsMatch(v) &&
                 digits is >= PhoneMinDigits and <= PhoneMaxDigits;
        return ok ? null : new FieldError(AdminErrors.FieldContactPhone, AdminErrors.MsgContactPhone);
    }

    public static FieldError? ContactEmail(string? value)
    {
        var v = Normalize(value);
        if (v.Length == 0) return null;
        return v.Length <= EmailMax && EmailPattern().IsMatch(v)
            ? null
            : new FieldError(AdminErrors.FieldContactEmail, AdminErrors.MsgContactEmail);
    }

    public static FieldError? Organization(string? value) =>
        Normalize(value).Length > OrganizationMax
            ? new FieldError(AdminErrors.FieldOrganization, AdminErrors.MsgOrganization)
            : null;

    public static FieldError? Notes(string? value) =>
        Normalize(value).Length > NotesMax ? new FieldError(AdminErrors.FieldNotes, AdminErrors.MsgNotes) : null;

    public static FieldError? Reason(string? value) =>
        Normalize(value).Length > ReasonMax ? new FieldError(AdminErrors.FieldReason, AdminErrors.MsgReason) : null;

    /// <summary>Primer error del alta, en el orden de la tabla de §6.4.</summary>
    public static FieldError? ValidateCreate(string? dni, string? name, string? sigla, string? contactPhone,
        string? contactEmail, string? organization, string? notes) =>
        Dni(dni) ?? ValidateEditable(name, sigla, contactPhone, contactEmail, organization, notes);

    /// <summary>Primer error de los 6 campos editables, en el orden de §6.4.</summary>
    public static FieldError? ValidateEditable(string? name, string? sigla, string? contactPhone,
        string? contactEmail, string? organization, string? notes) =>
        Name(name) ?? Sigla(sigla) ?? ContactPhone(contactPhone) ?? ContactEmail(contactEmail) ??
        Organization(organization) ?? Notes(notes);
}
