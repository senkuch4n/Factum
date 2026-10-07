using Factum.Backend.Common;

namespace Factum.Backend.Services.Admin;

/// <summary>
/// Códigos y textos del panel de cuentas (abm-clientes §5.1 y contrato §8.2/§8.3). Los textos tienen que
/// coincidir con <c>client/src/lib/admin-accounts.ts</c> (<c>ADMIN_MESSAGES</c>).
/// </summary>
public static class AdminErrors
{
    // ── códigos (§8.2) ───────────────────────────────────────────────────────
    public const string ValidationFailed = "validation_failed";
    public const string UserNotFound = "user_not_found";
    public const string DniTaken = "dni_taken";
    public const string StaleUpdate = "stale_update";
    public const string CannotActOnSelf = "cannot_act_on_self";
    public const string LastSuperadmin = "last_superadmin";
    public const string InvalidState = "invalid_state";
    public const string OperationBusy = "operation_busy";
    public const string NotAvailable = "not_available";

    // ── campos del error (AdminUserField) ────────────────────────────────────
    public const string FieldDni = "dni";
    public const string FieldName = "name";
    public const string FieldSigla = "sigla";
    public const string FieldContactPhone = "contact_phone";
    public const string FieldContactEmail = "contact_email";
    public const string FieldOrganization = "organization";
    public const string FieldNotes = "notes";
    public const string FieldReason = "reason";
    public const string FieldExpectedUpdatedAt = "expected_updated_at";

    /// <summary>Clave extra de <c>dni_taken</c>.</summary>
    public const string ExistingUserIdKey = "existing_user_id";

    // ── textos (§8.3) ────────────────────────────────────────────────────────
    public const string MsgNotAvailable = "La administración de cuentas no está disponible en este modo.";
    public const string MsgUserNotFound = "No existe esa cuenta.";
    public const string MsgDniTaken = "Ya existe una cuenta con ese DNI.";
    public const string MsgStaleUpdate = "Otro superadmin modificó esta cuenta. Recargá para ver los cambios.";
    public const string MsgCannotActOnSelf =
        "No podés hacer esto sobre tu propia cuenta. Para cambiar tu contraseña usá \"Cambiar contraseña\".";
    public const string MsgLastSuperadmin = "No se puede: el sistema tiene que tener al menos un superadmin activo.";
    public const string MsgAlreadySuspended = "La cuenta ya está suspendida.";
    public const string MsgAlreadyActive = "La cuenta ya está activa.";
    public const string MsgNotLocked = "La cuenta no está bloqueada.";
    public const string MsgOperationBusy = "Otra operación sobre los superadmins está en curso. Probá de nuevo.";

    public const string MsgDni = "El DNI tiene que tener 7 u 8 dígitos.";
    public const string MsgNameRequired = "El nombre es obligatorio.";
    public const string MsgNameTooLong = "El nombre puede tener hasta 120 caracteres.";
    public const string MsgSigla = "La sigla puede tener hasta 30 caracteres.";
    public const string MsgContactPhone =
        "Ingresá un teléfono válido: números, espacios, guiones, paréntesis y un + al inicio.";
    public const string MsgContactEmail = "Ingresá un email válido.";
    public const string MsgOrganization = "La organización puede tener hasta 120 caracteres.";
    public const string MsgNotes = "Las notas pueden tener hasta 1000 caracteres.";
    public const string MsgReason = "El motivo puede tener hasta 300 caracteres.";
    public const string MsgExpectedUpdatedAt = "Falta la versión de la cuenta. Recargá e intentá de nuevo.";

    /// <summary>
    /// Error con <c>code</c> (y <c>field</c> / extras) en <see cref="Result{T}.Details"/>, con el
    /// <see cref="ErrorKind"/> que corresponde al código.
    /// </summary>
    public static Result<T> Fail<T>(string code, string message, string? field = null,
        IReadOnlyDictionary<string, object?>? extra = null)
    {
        var details = new Dictionary<string, object?> { ["code"] = code };
        if (field is not null) details["field"] = field;
        if (extra is not null)
            foreach (var (k, v) in extra) details[k] = v;
        return Result.Fail<T>(KindFor(code), message, details);
    }

    public static Result<T> Fail<T>(FieldError error) => Fail<T>(ValidationFailed, error.Message, error.Field);

    public static ErrorKind KindFor(string code) => code switch
    {
        ValidationFailed => ErrorKind.Validation,
        UserNotFound or NotAvailable => ErrorKind.NotFound,
        DniTaken or StaleUpdate or CannotActOnSelf or LastSuperadmin or InvalidState or OperationBusy
            => ErrorKind.Conflict,
        // marca-por-cliente §6.5 (B7).
        Branding.BrandingErrors.ImageNotFound => ErrorKind.NotFound,
        Branding.BrandingErrors.RequestTooLarge => ErrorKind.PayloadTooLarge,
        _ => ErrorKind.Failure,
    };

    /// <summary>El <c>code</c> de un Result fallido, o null.</summary>
    public static string? CodeOf<T>(Result<T> result) =>
        result.Details is { } d && d.TryGetValue("code", out var c) ? c as string : null;
}

/// <summary>Error de validación de un campo (nombre JSON + texto de §8.3).</summary>
public sealed record FieldError(string Field, string Message);
