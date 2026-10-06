using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Factum.Backend.Services.Admin;
using Factum.Backend.Services.Auth;

namespace Factum.Backend.Services.Branding;

/// <summary>Quién guarda: DNI y nombre de la sesión, y su IP.</summary>
public sealed record BrandingActor(string Dni, string Name, string? Ip);

/// <summary>Marca del informe por cuenta (marca-por-cliente §6.3).</summary>
public interface IAccountBrandingService
{
    Task<Result<AccountBrandingResponse>> GetOwnAsync(User user, CancellationToken ct = default);
    Task<Result<AccountBrandingSaveResponse>> SaveOwnAsync(BrandingActor actor, User user, BrandingSaveInput input,
        CancellationToken ct = default);
    Task<Result<AccountBrandingImage>> GetOwnImageAsync(string dni, BrandingImageKind kind, CancellationToken ct = default);

    // Panel (solo local; id = users._id).
    Task<Result<AccountBrandingResponse>> GetForAccountAsync(string userId, CancellationToken ct = default);
    Task<Result<AccountBrandingSaveResponse>> SaveForAccountAsync(BrandingActor actor, string userId,
        BrandingSaveInput input, CancellationToken ct = default);
    Task<Result<AccountBrandingImage>> GetAccountImageAsync(string userId, BrandingImageKind kind,
        CancellationToken ct = default);
}

/// <summary>
/// marca-por-cliente §6.3. El dueño de la marca propia sale SIEMPRE de la sesión (nunca de un parámetro). El
/// del panel, de <c>users._id</c> (solo <c>Auth:Mode=local</c>). Guardado: validar → normalizar → leer →
/// diff (sin cambios = <c>changed:false</c> sin escribir ni auditar) → control optimista → escribir → auditar
/// → releer. Escribe solo en <c>account_brandings</c> (por <c>_id</c>) e inserta en <c>user_admin_events</c>;
/// <c>users</c> solo se lee. Nunca loguea bytes, <c>Changes</c> ni textos de contacto.
/// </summary>
public sealed class AccountBrandingService(
    AuthSettings settings,
    IAccountBrandingRepository brandings,
    IUserRepository users,
    IUserAdminEventRepository events,
    TimeProvider time,
    ILogger<AccountBrandingService> log) : IAccountBrandingService
{
    private const int AuditAttempts = 2;
    private const string OwnBasePath = "/api/branding";

    private bool IsLocal => settings.Mode == AuthModes.Local;
    private DateTime Now() => AuthTime.TruncateToMs(time.GetUtcNow().UtcDateTime);

    private static string PanelBasePath(string userId) => $"/api/admin/users/{Uri.EscapeDataString(userId)}/branding";

    // ── propia ───────────────────────────────────────────────────────────────

    public async Task<Result<AccountBrandingResponse>> GetOwnAsync(User user, CancellationToken ct = default)
    {
        var doc = await brandings.FindMetaAsync(user.Dni, ct);
        var suggestion = doc is null ? await OwnSuggestionAsync(user.Dni, ct) : null;
        return Result.Ok(new AccountBrandingResponse(ToDto(doc, OwnBasePath, suggestion)));
    }

    public Task<Result<AccountBrandingSaveResponse>> SaveOwnAsync(BrandingActor actor, User user,
        BrandingSaveInput input, CancellationToken ct = default) =>
        SaveAsync(actor, new Owner(user.Dni, user.Name, null, OwnBasePath), input, ct);

    public Task<Result<AccountBrandingImage>> GetOwnImageAsync(string dni, BrandingImageKind kind,
        CancellationToken ct = default) => ImageAsync(dni, kind, ct);

    // ── panel ────────────────────────────────────────────────────────────────

    public async Task<Result<AccountBrandingResponse>> GetForAccountAsync(string userId, CancellationToken ct = default)
    {
        if (!IsLocal) return NotAvailable<AccountBrandingResponse>();
        var acc = await users.FindAdminViewByIdAsync(userId, ct);
        if (acc is null) return UserNotFound<AccountBrandingResponse>();
        var doc = await brandings.FindMetaAsync(acc.Dni, ct);
        return Result.Ok(new AccountBrandingResponse(ToDto(doc, PanelBasePath(acc.Id), doc is null ? Suggest(acc) : null)));
    }

    public async Task<Result<AccountBrandingSaveResponse>> SaveForAccountAsync(BrandingActor actor, string userId,
        BrandingSaveInput input, CancellationToken ct = default)
    {
        if (!IsLocal) return NotAvailable<AccountBrandingSaveResponse>();
        var acc = await users.FindAdminViewByIdAsync(userId, ct);
        if (acc is null) return UserNotFound<AccountBrandingSaveResponse>();
        return await SaveAsync(actor, new Owner(acc.Dni, acc.Name, acc, PanelBasePath(acc.Id)), input, ct);
    }

    public async Task<Result<AccountBrandingImage>> GetAccountImageAsync(string userId, BrandingImageKind kind,
        CancellationToken ct = default)
    {
        if (!IsLocal) return NotAvailable<AccountBrandingImage>();
        var acc = await users.FindAdminViewByIdAsync(userId, ct);
        if (acc is null) return UserNotFound<AccountBrandingImage>();
        return await ImageAsync(acc.Dni, kind, ct);
    }

    // ── guardado (los dos caminos) ───────────────────────────────────────────

    /// <summary>Dueño de la marca. <c>Account</c> no es null solo en el panel.</summary>
    private sealed record Owner(string Dni, string Name, UserAccount? Account, string BasePath);

    private async Task<Result<AccountBrandingSaveResponse>> SaveAsync(BrandingActor actor, Owner owner,
        BrandingSaveInput input, CancellationToken ct)
    {
        // 1-2. Validar y normalizar. Si falla, ni se escribe ni se audita.
        var (normalized, error) = BrandingRules.Validate(input);
        if (error is not null)
            return AdminErrors.Fail<AccountBrandingSaveResponse>(BrandingErrors.ValidationFailed, error.Message,
                error.Field, error.Extra());
        var after = normalized!;

        // 3. Leer (sin bytes).
        var before = await brandings.FindMetaAsync(owner.Dni, ct);

        // 4. Diff. Un replace con la misma versión que la actual no es cambio (y no se reescribe).
        after = after with
        {
            Logo = SameImage(before?.Logo, after.Logo) ? ImageChange.Keep : after.Logo,
            Isotype = SameImage(before?.Isotype, after.Isotype) ? ImageChange.Keep : after.Isotype,
        };
        var changes = BrandingChanges.Diff(before, after);
        if (changes.Count == 0)
        {
            // Sin cambios (o formulario vacío sin documento): sin escribir, sin auditar y sin mirar la versión.
            var suggestion = before is null ? await SuggestionAsync(owner, ct) : null;
            return Result.Ok(new AccountBrandingSaveResponse(ToDto(before, owner.BasePath, suggestion), Changed: false));
        }

        // 5. Control optimista.
        var now = Now();
        var expectedRaw = input.Metadata.ExpectedUpdatedAt;
        if (before is null)
        {
            if (expectedRaw is not null) return Stale();
            var doc = new AccountBranding
            {
                Id = owner.Dni,
                OrganizationName = after.OrganizationName,
                ContactLines = after.ContactLines,
                PrimaryColor = after.PrimaryColor,
                AccentColor = after.AccentColor,
                Logo = after.Logo.Action == BrandingImageAction.Replace ? after.Logo.Image : null,
                Isotype = after.Isotype.Action == BrandingImageAction.Replace ? after.Isotype.Image : null,
                CreatedAt = now,
                UpdatedAt = now,
                UpdatedBy = actor.Dni,
            };
            if (!await brandings.TryInsertAsync(doc, ct)) return Stale();
        }
        else
        {
            if (expectedRaw is not { } raw) return Stale();
            var expected = AuthTime.TruncateToMs(raw.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(raw, DateTimeKind.Utc)
                : raw);
            if (expected.Ticks != AuthTime.TruncateToMs(before.UpdatedAt).Ticks) return Stale();
            var update = new BrandingUpdate(after.OrganizationName, after.ContactLines, after.PrimaryColor,
                after.AccentColor, after.Logo, after.Isotype, now, actor.Dni);
            if (!await brandings.UpdateIfUnchangedAsync(owner.Dni, update, before.UpdatedAt, ct)) return Stale();
        }

        log.LogInformation("Marca guardada para DNI {Dni} por {ActorDni} (campos: {Fields})",
            owner.Dni, actor.Dni, string.Join(", ", changes.Select(c => c.Field)));

        // 6. Auditar después de escribir. Si falla, la respuesta igual es éxito.
        await AuditAsync(actor, owner, changes, ct);

        // 7. Releer.
        var saved = await brandings.FindMetaAsync(owner.Dni, ct);
        return Result.Ok(new AccountBrandingSaveResponse(ToDto(saved, owner.BasePath, null), Changed: true));
    }

    private static bool SameImage(AccountBrandingImage? current, ImageChange change) =>
        change.Action == BrandingImageAction.Replace && current is not null &&
        string.Equals(current.Version, change.Image?.Version, StringComparison.Ordinal);

    private static Result<AccountBrandingSaveResponse> Stale() =>
        AdminErrors.Fail<AccountBrandingSaveResponse>(BrandingErrors.StaleUpdate, BrandingErrors.MsgStaleBranding);

    private async Task AuditAsync(BrandingActor actor, Owner owner, List<UserAdminChange> changes, CancellationToken ct)
    {
        // TargetUserId: en el panel, la cuenta; la propia en local, el _id de users (o "" si no hay cuenta);
        // en dev/external, "" (DT6: no existe users y no se consulta).
        var targetUserId = "";
        if (owner.Account is { } acc) targetUserId = acc.Id;
        else if (IsLocal)
        {
            try
            {
                targetUserId = (await users.FindSessionByDniAsync(owner.Dni, ct))?.Id ?? "";
            }
            catch (Exception)
            {
                log.LogWarning("Auditoría de marca: no se pudo resolver la cuenta del DNI {Dni}", owner.Dni);
            }
        }

        var evt = new UserAdminEvent
        {
            At = Now(),
            ActorDni = actor.Dni,
            ActorName = actor.Name,
            TargetUserId = targetUserId,
            TargetDni = owner.Dni,
            TargetName = owner.Name,
            Action = UserAdminActions.UpdateBranding,
            Changes = changes,
            Reason = null,
            Ip = actor.Ip,
        };
        for (var attempt = 1; attempt <= AuditAttempts; attempt++)
        {
            try
            {
                await events.InsertAsync(evt, CancellationToken.None);
                return;
            }
            catch (Exception) when (attempt < AuditAttempts)
            {
                // Reintento.
            }
            catch (Exception)
            {
                // Sin la excepción: su texto podría arrastrar el documento (Changes).
                log.LogError("Auditoría no registrada: {Action} sobre el DNI {TargetDni} por DNI {ActorDni}",
                    UserAdminActions.UpdateBranding, owner.Dni, actor.Dni);
            }
        }
    }

    // ── imágenes ─────────────────────────────────────────────────────────────

    private async Task<Result<AccountBrandingImage>> ImageAsync(string dni, BrandingImageKind kind, CancellationToken ct)
    {
        var img = await brandings.FindImageAsync(dni, kind, ct);
        if (img is null || img.Data is not { Length: > 0 })
            return AdminErrors.Fail<AccountBrandingImage>(BrandingErrors.ImageNotFound, BrandingErrors.MsgBrandingImageNotFound);
        return Result.Ok(img);
    }

    // ── sugerencia (D10) ─────────────────────────────────────────────────────

    private Task<string?> SuggestionAsync(Owner owner, CancellationToken ct) =>
        owner.Account is { } acc ? Task.FromResult(Suggest(acc)) : OwnSuggestionAsync(owner.Dni, ct);

    /// <summary>Solo en local (fuera de local no se consulta users).</summary>
    private async Task<string?> OwnSuggestionAsync(string dni, CancellationToken ct)
    {
        if (!IsLocal) return null;
        return Suggest(await users.FindSessionByDniAsync(dni, ct));
    }

    private string? Suggest(UserAccount? acc)
    {
        if (!IsLocal || acc is null) return null;
        var org = acc.Organization?.Trim() ?? "";
        return org.Length == 0 ? null : org;
    }

    // ── DTO ──────────────────────────────────────────────────────────────────

    internal static AccountBrandingDto ToDto(AccountBranding? doc, string basePath, string? suggestion)
    {
        if (doc is null)
            return new AccountBrandingDto(false, "", [], null, null, null, null, null, null, suggestion);
        return new AccountBrandingDto(
            true,
            doc.OrganizationName ?? "",
            doc.ContactLines ?? [],
            ColorOrNull(doc.PrimaryColor),
            ColorOrNull(doc.AccentColor),
            ImageDto(doc.Logo, $"{basePath}/logo"),
            ImageDto(doc.Isotype, $"{basePath}/isotype"),
            DateTime.SpecifyKind(AuthTime.TruncateToMs(doc.UpdatedAt), DateTimeKind.Utc),
            doc.UpdatedBy,
            null);
    }

    private static string? ColorOrNull(string? hex) => string.IsNullOrEmpty(hex) ? null : "#" + hex;

    private static BrandingImageDto? ImageDto(AccountBrandingImage? img, string path) =>
        img is null
            ? null
            : new BrandingImageDto($"{path}?v={Uri.EscapeDataString(img.Version)}", img.ContentType, img.Width,
                img.Height, img.Size, img.Version);

    private static Result<T> NotAvailable<T>() => AdminErrors.Fail<T>(BrandingErrors.NotAvailable, AdminErrors.MsgNotAvailable);
    private static Result<T> UserNotFound<T>() => AdminErrors.Fail<T>(BrandingErrors.UserNotFound, AdminErrors.MsgUserNotFound);
}
