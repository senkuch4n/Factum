using Factum.Backend.Common;
using Factum.Backend.Infrastructure;
using Factum.Backend.Models;

namespace Factum.Backend.Services.Auth;

/// <summary>
/// Arranque de <c>Auth:Mode=local</c> (usuarios-locales §7.2): índices, superadmins iniciales (D9),
/// reset de emergencia (D10) y chequeo de que quede al menos un superadmin activo. Solo escribe en
/// <c>users</c>; nunca toca casos, perfiles ni catálogos (D11). Ningún log lleva contraseñas ni hashes.
/// </summary>
public sealed class LocalUserBootstrapper(
    AuthSettings settings,
    IUserRepository users,
    IUserAccountService accounts,
    IPasswordHasher hasher,
    TimeProvider time,
    ILogger<LocalUserBootstrapper> logger)
{
    public const string BootstrapCreatedBy = "bootstrap";

    internal const string NoSuperadminMessage =
        "Auth:Mode=local necesita al menos un superadmin activo y la colección users no tiene ninguno. Configurá " +
        "Auth:Local:BootstrapSuperadmins:0:Dni, Auth:Local:BootstrapSuperadmins:0:Name y " +
        "Auth:Local:BootstrapSuperadmins:0:TemporaryPassword (o Auth__Local__BootstrapSuperadmins__0__Dni, … como " +
        "variables de entorno), o Auth:Local:ResetSuperadmin para reactivar uno existente.";

    /// <summary>Devuelve la cantidad de superadmins activos. Tira si no queda ninguno.</summary>
    public async Task<long> RunAsync(CancellationToken ct)
    {
        var opts = settings.Local ?? new LocalAuthOptions();

        // 1. Índices (el único de DNI hace seguro el bootstrap concurrente).
        await users.EnsureIndexesAsync(ct);

        // 2. Superadmins iniciales: si ya existen, no se modifica nada.
        foreach (var sa in opts.BootstrapSuperadmins)
        {
            var existing = await users.FindByDniAsync(sa.Dni, ct);
            if (existing is null)
            {
                var created = await accounts.CreateAsync(
                    new NewUserAccount(sa.Dni, sa.Name, "", UserRoles.Superadmin, sa.TemporaryPassword),
                    BootstrapCreatedBy, ct);
                if (created.IsSuccess)
                {
                    logger.LogInformation("Superadmin inicial creado: DNI {Dni}", sa.Dni);
                    continue;
                }
                if (created.Kind != ErrorKind.Conflict)
                    throw new InvalidOperationException(
                        $"No se pudo crear el superadmin inicial DNI {sa.Dni}: {created.Error}");
                // Conflict: otra réplica lo creó entre la búsqueda y el insert → "ya existe".
                existing = await users.FindByDniAsync(sa.Dni, ct);
            }

            logger.LogInformation("Superadmin inicial ya existe: DNI {Dni}; no se modifica", sa.Dni);
            if (existing is not null && existing.Role != UserRoles.Superadmin)
                logger.LogWarning("El superadmin inicial DNI {Dni} existe con rol {Role}; no se modifica", sa.Dni, existing.Role);
            if (existing is not null && existing.Status == UserStatuses.Suspendido)
                logger.LogWarning("El superadmin inicial DNI {Dni} existe y está suspendido; no se modifica", sa.Dni);
        }

        // 3. Reset de emergencia, idempotente por la marca hasheada.
        if (opts.ResetSuperadmin is { } reset)
        {
            var acc = await users.FindByDniAsync(reset.Dni, ct);
            if (acc is null || acc.Role != UserRoles.Superadmin)
            {
                logger.LogWarning("Auth:Local:ResetSuperadmin se ignora: el DNI {Dni} no es un superadmin", reset.Dni);
            }
            else if (acc.LastEmergencyResetHash is { } marker && hasher.Verify(reset.TemporaryPassword, marker))
            {
                logger.LogWarning("Auth:Local:ResetSuperadmin ya se aplicó para DNI {Dni}; borralo de la configuración",
                    reset.Dni);
            }
            else
            {
                var now = AuthTime.TruncateToMs(time.GetUtcNow().UtcDateTime);
                await users.ApplyEmergencyResetAsync(acc.Id, hasher.Hash(reset.TemporaryPassword),
                    hasher.Hash(reset.TemporaryPassword), now, ct);
                logger.LogWarning(
                    "Reset de emergencia aplicado al superadmin DNI {Dni}. Borrá Auth:Local:ResetSuperadmin de la configuración.",
                    reset.Dni);
            }
        }

        // 4. Tiene que quedar al menos un superadmin activo.
        var active = await users.CountActiveSuperadminsAsync(ct);
        if (active == 0)
            throw new InvalidOperationException(NoSuperadminMessage);

        // 5. Resumen.
        logger.LogInformation("Auth: modo local ({N} superadmins activos)", active);
        return active;
    }
}
