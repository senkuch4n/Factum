using Factum.Backend.Infrastructure;
using Factum.Backend.Models;

namespace Factum.Backend.Services.Admin;

/// <summary>
/// Diff para la auditoría (abm-clientes §6.5). Pura y con LISTA BLANCA de campos: ningún campo de
/// contraseña (temporal, hash, marca de emergencia) puede entrar en <c>Changes</c>.
/// </summary>
public static class AdminChanges
{
    /// <summary>Campos que pueden aparecer en <c>Changes</c>, con su nombre JSON.</summary>
    public static readonly IReadOnlyList<string> AllowedFields =
        ["dni", "name", "sigla", "role", "contact_phone", "contact_email", "organization", "notes"];

    /// <summary>Cada campo no vacío de la cuenta creada, con <c>From = null</c>.</summary>
    public static List<UserAdminChange> ForCreate(UserAccount created)
    {
        var values = new (string Field, string? Value)[]
        {
            ("dni", created.Dni),
            ("name", created.Name),
            ("sigla", created.Sigla),
            ("role", created.Role),
            ("contact_phone", created.ContactPhone),
            ("contact_email", created.ContactEmail),
            ("organization", created.Organization),
            ("notes", created.Notes),
        };
        return values
            .Select(v => (v.Field, Value: AccountFieldRules.Normalize(v.Value)))
            .Where(v => v.Value.Length > 0)
            .Select(v => new UserAdminChange { Field = v.Field, From = null, To = v.Value })
            .ToList();
    }

    /// <summary>
    /// Los campos editables que cambiaron (<c>from</c> → <c>to</c>). Comparación ordinal después de
    /// normalizar: un cambio solo de espacios al borde no cuenta. <c>dni</c> y <c>role</c> no se editan.
    /// </summary>
    public static List<UserAdminChange> ForUpdate(UserAccount before, AccountEditableFields after)
    {
        var pairs = new (string Field, string? From, string To)[]
        {
            ("name", before.Name, after.Name),
            ("sigla", before.Sigla, after.Sigla),
            ("contact_phone", before.ContactPhone, after.ContactPhone),
            ("contact_email", before.ContactEmail, after.ContactEmail),
            ("organization", before.Organization, after.Organization),
            ("notes", before.Notes, after.Notes),
        };
        var list = new List<UserAdminChange>();
        foreach (var (field, from, to) in pairs)
        {
            var f = AccountFieldRules.Normalize(from);
            var t = AccountFieldRules.Normalize(to);
            if (!string.Equals(f, t, StringComparison.Ordinal))
                list.Add(new UserAdminChange { Field = field, From = f, To = t });
        }
        return list;
    }
}
