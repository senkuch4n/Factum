using System.Text.Json;
using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Admin;
using Factum.Backend.Services.Auth;
using Microsoft.Extensions.Logging;

namespace Factum.Backend.Tests.Admin;

/// <summary>abm-clientes §6 (B25-B31, B34), sobre los dobles en memoria.</summary>
public sealed class UserAdminServiceTests
{
    private const string ClientDni = "30111222";

    private static AdminCreateUserRequest CreateReq(string dni = ClientDni, string name = "Ana Pérez",
        string? phone = "+54 11 4555-1234", string? email = "ana@estudio.com", string? org = "Estudio Pérez",
        string? notes = "Cliente nuevo\nPaga por transferencia") =>
        new(dni, name, "AP", phone, email, org, notes);

    private static AdminUpdateUserRequest UpdateReq(UserAccount acc, string? name = null, string? phone = null,
        DateTime? expected = null) =>
        new(name ?? acc.Name, acc.Sigla, phone ?? acc.ContactPhone, acc.ContactEmail, acc.Organization, acc.Notes,
            expected ?? acc.UpdatedAt);

    private static string? Code<T>(Factum.Backend.Common.Result<T> r) => AdminErrors.CodeOf(r);

    private static TokenPayload TokenFor(UserAccount acc) =>
        new(new User { Dni = acc.Dni, Name = acc.Name, Sigla = acc.Sigla }, AuthTime.ToUnixMs(acc.PasswordChangedAt));

    // ── B25 alta ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ClienteActivoMustChange_CreatedByActor()
    {
        var f = new AdminFixture();
        var (owner, _) = await f.SeedSuperadminsAsync();

        var r = await f.Service.CreateAsync(AdminFixture.ActorFor(owner), CreateReq());

        Assert.True(r.IsSuccess, r.Error);
        var doc = f.Doc(ClientDni);
        Assert.Equal(UserRoles.Cliente, doc.Role);
        Assert.Equal(UserStatuses.Activo, doc.Status);
        Assert.True(doc.MustChangePassword);
        Assert.Equal(owner.Dni, doc.CreatedBy);
        Assert.Equal("+54 11 4555-1234", doc.ContactPhone);
        Assert.Equal("ana@estudio.com", doc.ContactEmail);
        Assert.Equal("Estudio Pérez", doc.Organization);
        Assert.Equal("Cliente nuevo\nPaga por transferencia", doc.Notes);

        var dto = r.Value!.User;
        Assert.Equal(doc.Id, dto.Id);
        Assert.Equal("Dueño", dto.CreatedByName);
        Assert.Equal(0, dto.CaseCount);
        Assert.Equal(f.Generator.Issued.Single(), r.Value.TemporaryPassword);
        Assert.True(f.Auth.Hasher.Verify(r.Value.TemporaryPassword, doc.PasswordHash));
    }

    [Fact]
    public async Task Create_AuditsWithoutPassword()
    {
        var f = new AdminFixture();
        var (owner, _) = await f.SeedSuperadminsAsync();

        var r = await f.Service.CreateAsync(AdminFixture.ActorFor(owner), CreateReq());

        var evt = Assert.Single(f.Events.All);
        Assert.Equal(UserAdminActions.Create, evt.Action);
        Assert.Equal(owner.Dni, evt.ActorDni);
        Assert.Equal("Dueño", evt.ActorName);
        Assert.Equal(r.Value!.User.Id, evt.TargetUserId);
        Assert.Equal(ClientDni, evt.TargetDni);
        Assert.Equal("10.0.0.7", evt.Ip);
        Assert.All(evt.Changes, c => Assert.Null(c.From));
        Assert.Equal(["dni", "name", "sigla", "role", "contact_phone", "contact_email", "organization", "notes"],
            evt.Changes.Select(c => c.Field));
        Assert.Equal("cliente", evt.Changes.Single(c => c.Field == "role").To);

        var json = JsonSerializer.Serialize(f.Events.All);
        Assert.DoesNotContain(r.Value.TemporaryPassword, json);
        Assert.DoesNotContain("pbkdf2-sha256", json);
        Assert.DoesNotContain(r.Value.TemporaryPassword, string.Join("\n", f.Log.Messages));
    }

    [Fact]
    public async Task Create_EmptyOptionalFields_NotInChanges()
    {
        var f = new AdminFixture();
        var (owner, _) = await f.SeedSuperadminsAsync();
        await f.Service.CreateAsync(AdminFixture.ActorFor(owner),
            new AdminCreateUserRequest(ClientDni, "Ana", null, null, "", null, null));
        Assert.Equal(["dni", "name", "role"], Assert.Single(f.Events.All).Changes.Select(c => c.Field));
    }

    [Fact]
    public async Task Create_DuplicateDni_DniTakenWithExistingId_NoAuditNoInsert()
    {
        var f = new AdminFixture();
        var (owner, _) = await f.SeedSuperadminsAsync();
        var existing = await f.SeedAsync(ClientDni, "Ana Original");
        var before = f.Repo.All.Count;

        var r = await f.Service.CreateAsync(AdminFixture.ActorFor(owner), CreateReq());

        Assert.False(r.IsSuccess);
        Assert.Equal(AdminErrors.DniTaken, Code(r));
        Assert.Equal(AdminErrors.MsgDniTaken, r.Error);
        Assert.Equal(existing.Id, r.Details![AdminErrors.ExistingUserIdKey]);
        Assert.Equal("dni", r.Details["field"]);
        Assert.Equal(before, f.Repo.All.Count);
        Assert.Equal("Ana Original", f.Doc(ClientDni).Name);
        Assert.Empty(f.Events.All);
    }

    [Theory]
    [InlineData("123", "Ana", null, "dni")]
    [InlineData(ClientDni, "", null, "name")]
    [InlineData(ClientDni, "Ana", "12345", "contact_phone")]
    public async Task Create_Invalid_ValidationFailedWithField_NoAudit(string dni, string name, string? phone, string field)
    {
        var f = new AdminFixture();
        var (owner, _) = await f.SeedSuperadminsAsync();

        var r = await f.Service.CreateAsync(AdminFixture.ActorFor(owner), CreateReq(dni, name, phone));

        Assert.Equal(AdminErrors.ValidationFailed, Code(r));
        Assert.Equal(field, r.Details!["field"]);
        Assert.Empty(f.Events.All);
        Assert.Null(f.Repo.Get(dni));
        Assert.Empty(f.Generator.Issued);
    }

    // ── B26 edición ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ChangesEditableFields_AuditsFromTo()
    {
        var f = new AdminFixture();
        var (owner, _) = await f.SeedSuperadminsAsync();
        var acc = await f.SeedAsync(ClientDni, "Ana Pérez");
        f.Time.Advance(TimeSpan.FromMinutes(1));

        var r = await f.Service.UpdateAsync(AdminFixture.ActorFor(owner), acc.Id,
            UpdateReq(acc, name: "Ana María Pérez", phone: "011 4555 1234"));

        Assert.True(r.IsSuccess, r.Error);
        Assert.True(r.Value!.Changed);
        var doc = f.Doc(ClientDni);
        Assert.Equal("Ana María Pérez", doc.Name);
        Assert.Equal("011 4555 1234", doc.ContactPhone);
        Assert.Equal(f.Now, doc.UpdatedAt);
        Assert.Equal(doc.UpdatedAt, r.Value.User.UpdatedAt);
        Assert.Equal(ClientDni, doc.Dni);
        Assert.Equal(UserRoles.Cliente, doc.Role);

        var evt = Assert.Single(f.Events.All);
        Assert.Equal(UserAdminActions.Update, evt.Action);
        Assert.Collection(evt.Changes,
            c => { Assert.Equal("name", c.Field); Assert.Equal("Ana Pérez", c.From); Assert.Equal("Ana María Pérez", c.To); },
            c => { Assert.Equal("contact_phone", c.Field); Assert.Equal("", c.From); Assert.Equal("011 4555 1234", c.To); });
    }

    [Fact]
    public async Task Update_NoChanges_NotWrittenNorAudited_EvenWithOldVersion()
    {
        var f = new AdminFixture();
        var (owner, _) = await f.SeedSuperadminsAsync();
        var acc = await f.SeedAsync(ClientDni, "Ana Pérez");

        var r = await f.Service.UpdateAsync(AdminFixture.ActorFor(owner), acc.Id,
            UpdateReq(acc, name: "  Ana Pérez  ", expected: acc.UpdatedAt.AddDays(-1)));

        Assert.True(r.IsSuccess);
        Assert.False(r.Value!.Changed);
        Assert.Equal(0, f.Repo.EditableWrites);
        Assert.Empty(f.Events.All);
    }

    [Fact]
    public async Task Update_StaleVersion_StaleUpdate_NoWrite()
    {
        var f = new AdminFixture();
        var (owner, _) = await f.SeedSuperadminsAsync();
        var acc = await f.SeedAsync(ClientDni, "Ana Pérez");

        var r = await f.Service.UpdateAsync(AdminFixture.ActorFor(owner), acc.Id,
            UpdateReq(acc, name: "Otro", expected: acc.UpdatedAt.AddMilliseconds(-1)));

        Assert.Equal(AdminErrors.StaleUpdate, Code(r));
        Assert.Equal(AdminErrors.MsgStaleUpdate, r.Error);
        Assert.Equal("Ana Pérez", f.Doc(ClientDni).Name);
        Assert.Equal(0, f.Repo.EditableWrites);
        Assert.Empty(f.Events.All);
    }

    [Fact]
    public async Task Update_TwoEditorsSameVersion_SecondGetsStale()
    {
        var f = new AdminFixture();
        var (owner, leo) = await f.SeedSuperadminsAsync();
        var acc = await f.SeedAsync(ClientDni, "Ana Pérez");
        f.Time.Advance(TimeSpan.FromSeconds(5));

        var first = await f.Service.UpdateAsync(AdminFixture.ActorFor(owner), acc.Id, UpdateReq(acc, phone: "1144445555"));
        f.Time.Advance(TimeSpan.FromSeconds(5));
        var second = await f.Service.UpdateAsync(AdminFixture.ActorFor(leo), acc.Id, UpdateReq(acc, phone: "1166667777"));

        Assert.True(first.IsSuccess);
        Assert.Equal(AdminErrors.StaleUpdate, Code(second));
        Assert.Equal("1144445555", f.Doc(ClientDni).ContactPhone);
    }

    [Fact]
    public async Task Update_FailedLoginInBetween_IsNotStale()
    {
        var f = new AdminFixture();
        var (owner, _) = await f.SeedSuperadminsAsync();
        var acc = await f.SeedAsync(ClientDni, "Ana Pérez");
        f.Time.Advance(TimeSpan.FromMinutes(1));

        // El cliente se equivoca de contraseña mientras el superadmin edita (T3).
        var login = await f.Auth.Provider.AuthenticateAsync(ClientDni, null, "equivocada-123");
        Assert.False(login.IsSuccess);
        Assert.Equal(1, f.Doc(ClientDni).FailedLoginCount);

        var r = await f.Service.UpdateAsync(AdminFixture.ActorFor(owner), acc.Id, UpdateReq(acc, name: "Ana B"));
        Assert.True(r.IsSuccess, r.Error);
        Assert.True(r.Value!.Changed);
    }

    [Fact]
    public async Task Update_LockoutInBetween_IsNotStale()
    {
        var f = new AdminFixture();
        var (owner, _) = await f.SeedSuperadminsAsync();
        var acc = await f.SeedAsync(ClientDni, "Ana Pérez");
        f.Time.Advance(TimeSpan.FromMinutes(1));
        for (var i = 0; i < f.Auth.Options.MaxFailedAttempts; i++)
            await f.Auth.Provider.AuthenticateAsync(ClientDni, null, "equivocada-123");
        Assert.NotNull(f.Doc(ClientDni).LockedUntil);

        var r = await f.Service.UpdateAsync(AdminFixture.ActorFor(owner), acc.Id, UpdateReq(acc, name: "Ana B"));
        Assert.True(r.IsSuccess, r.Error);
    }

    [Fact]
    public async Task Update_MissingExpected_ValidationFailed()
    {
        var f = new AdminFixture();
        var (owner, _) = await f.SeedSuperadminsAsync();
        var acc = await f.SeedAsync(ClientDni, "Ana Pérez");

        var r = await f.Service.UpdateAsync(AdminFixture.ActorFor(owner), acc.Id,
            new AdminUpdateUserRequest("Ana", null, null, null, null, null, null));

        Assert.Equal(AdminErrors.ValidationFailed, Code(r));
        Assert.Equal("expected_updated_at", r.Details!["field"]);
        Assert.Equal(AdminErrors.MsgExpectedUpdatedAt, r.Error);
    }

    [Fact]
    public async Task Update_Invalid_ValidationFailed_NoAudit()
    {
        var f = new AdminFixture();
        var (owner, _) = await f.SeedSuperadminsAsync();
        var acc = await f.SeedAsync(ClientDni, "Ana Pérez");

        var r = await f.Service.UpdateAsync(AdminFixture.ActorFor(owner), acc.Id,
            new AdminUpdateUserRequest("Ana", null, null, "sin-arroba", null, null, acc.UpdatedAt));

        Assert.Equal("contact_email", r.Details!["field"]);
        Assert.Empty(f.Events.All);
    }

    [Fact]
    public async Task Update_UnknownId_UserNotFound()
    {
        var f = new AdminFixture();
        var (owner, _) = await f.SeedSuperadminsAsync();
        var r = await f.Service.UpdateAsync(AdminFixture.ActorFor(owner), "no-existe",
            new AdminUpdateUserRequest("Ana", null, null, null, null, null, DateTime.UtcNow));
        Assert.Equal(AdminErrors.UserNotFound, Code(r));
    }

    // ── B27 suspender / reactivar ────────────────────────────────────────────

    [Fact]
    public async Task Suspend_SetsFields_AndCutsSession()
    {
        var f = new AdminFixture();
        var (owner, _) = await f.SeedSuperadminsAsync();
        var acc = await f.SeedAsync(ClientDni, "Ana Pérez");
        var token = TokenFor(acc);
        Assert.True((await f.Auth.Sessions.ValidateAsync(token, CancellationToken.None)).Ok);
        f.Time.Advance(TimeSpan.FromMinutes(1));

        var r = await f.Service.SuspendAsync(AdminFixture.ActorFor(owner), acc.Id, new AdminSuspendRequest("  No pagó  "));

        Assert.True(r.IsSuccess, r.Error);
        var doc = f.Doc(ClientDni);
        Assert.Equal(UserStatuses.Suspendido, doc.Status);
        Assert.Equal(f.Now, doc.SuspendedAt);
        Assert.Equal(owner.Dni, doc.SuspendedBy);
        Assert.Equal("No pagó", doc.SuspensionReason);
        Assert.Equal("Dueño", r.Value!.User.SuspendedByName);
        Assert.Equal("No pagó", r.Value.User.SuspensionReason);
        Assert.Equal(0, f.Locks.AcquireAttempts); // un cliente no toma el lock

        var check = await f.Auth.Sessions.ValidateAsync(token, CancellationToken.None);
        Assert.Equal(AuthErrors.AccountSuspended, check.FailureCode);

        var evt = Assert.Single(f.Events.All);
        Assert.Equal(UserAdminActions.Suspend, evt.Action);
        Assert.Equal("No pagó", evt.Reason);
        Assert.Empty(evt.Changes);
    }

    [Fact]
    public async Task Suspend_EmptyReason_IsNull()
    {
        var f = new AdminFixture();
        var (owner, _) = await f.SeedSuperadminsAsync();
        var acc = await f.SeedAsync(ClientDni, "Ana Pérez");

        var r = await f.Service.SuspendAsync(AdminFixture.ActorFor(owner), acc.Id, new AdminSuspendRequest("   "));
        Assert.True(r.IsSuccess);
        Assert.Null(f.Doc(ClientDni).SuspensionReason);
        Assert.Null(Assert.Single(f.Events.All).Reason);

        var r2 = await f.Service.ReactivateAsync(AdminFixture.ActorFor(owner), acc.Id);
        Assert.True(r2.IsSuccess);
        var r3 = await f.Service.SuspendAsync(AdminFixture.ActorFor(owner), acc.Id, null);
        Assert.True(r3.IsSuccess);
    }

    [Fact]
    public async Task Suspend_ReasonTooLong_ValidationFailed()
    {
        var f = new AdminFixture();
        var (owner, _) = await f.SeedSuperadminsAsync();
        var acc = await f.SeedAsync(ClientDni, "Ana Pérez");
        var r = await f.Service.SuspendAsync(AdminFixture.ActorFor(owner), acc.Id, new AdminSuspendRequest(new string('r', 301)));
        Assert.Equal(AdminErrors.ValidationFailed, Code(r));
        Assert.Equal("reason", r.Details!["field"]);
        Assert.Equal(UserStatuses.Activo, f.Doc(ClientDni).Status);
        Assert.Empty(f.Events.All);
    }

    [Fact]
    public async Task Reactivate_ClearsFields_LoginWithOldPasswordWorks()
    {
        var f = new AdminFixture();
        var (owner, _) = await f.SeedSuperadminsAsync();
        var acc = await f.SeedAsync(ClientDni, "Ana Pérez");
        await f.Service.SuspendAsync(AdminFixture.ActorFor(owner), acc.Id, new AdminSuspendRequest("motivo"));
        Assert.False((await f.Auth.Provider.AuthenticateAsync(ClientDni, null, AdminFixture.Pwd)).IsSuccess);

        var r = await f.Service.ReactivateAsync(AdminFixture.ActorFor(owner), acc.Id);

        Assert.True(r.IsSuccess, r.Error);
        var doc = f.Doc(ClientDni);
        Assert.Equal(UserStatuses.Activo, doc.Status);
        Assert.Null(doc.SuspendedAt);
        Assert.Null(doc.SuspendedBy);
        Assert.Null(doc.SuspensionReason);
        Assert.True((await f.Auth.Provider.AuthenticateAsync(ClientDni, null, AdminFixture.Pwd)).IsSuccess);
        Assert.Equal([UserAdminActions.Suspend, UserAdminActions.Reactivate], f.Events.All.Select(e => e.Action));
    }

    [Fact]
    public async Task InvalidState_BothDirections_NoAudit()
    {
        var f = new AdminFixture();
        var (owner, _) = await f.SeedSuperadminsAsync();
        var acc = await f.SeedAsync(ClientDni, "Ana Pérez");

        var reactivateActive = await f.Service.ReactivateAsync(AdminFixture.ActorFor(owner), acc.Id);
        Assert.Equal(AdminErrors.InvalidState, Code(reactivateActive));
        Assert.Equal(AdminErrors.MsgAlreadyActive, reactivateActive.Error);

        await f.Service.SuspendAsync(AdminFixture.ActorFor(owner), acc.Id, null);
        var suspendAgain = await f.Service.SuspendAsync(AdminFixture.ActorFor(owner), acc.Id, null);
        Assert.Equal(AdminErrors.InvalidState, Code(suspendAgain));
        Assert.Equal(AdminErrors.MsgAlreadySuspended, suspendAgain.Error);

        Assert.Single(f.Events.All);
    }

    [Fact]
    public async Task Suspend_Self_CannotActOnSelf_NoEvent()
    {
        var f = new AdminFixture();
        var (owner, _) = await f.SeedSuperadminsAsync();

        var r = await f.Service.SuspendAsync(AdminFixture.ActorFor(owner), owner.Id, null);

        Assert.Equal(AdminErrors.CannotActOnSelf, Code(r));
        Assert.Equal(AdminErrors.MsgCannotActOnSelf, r.Error);
        Assert.Equal(UserStatuses.Activo, f.Doc(owner.Dni).Status);
        Assert.Empty(f.Events.All);
        Assert.Equal(0, f.Locks.AcquireAttempts);
    }

    [Fact]
    public async Task UnknownId_UserNotFound_ForEveryAction()
    {
        var f = new AdminFixture();
        var (owner, _) = await f.SeedSuperadminsAsync();
        var a = AdminFixture.ActorFor(owner);
        Assert.Equal(AdminErrors.UserNotFound, Code(await f.Service.GetAsync("x")));
        Assert.Equal(AdminErrors.UserNotFound, Code(await f.Service.SuspendAsync(a, "x", null)));
        Assert.Equal(AdminErrors.UserNotFound, Code(await f.Service.ReactivateAsync(a, "x")));
        Assert.Equal(AdminErrors.UserNotFound, Code(await f.Service.ResetPasswordAsync(a, "x")));
        Assert.Equal(AdminErrors.UserNotFound, Code(await f.Service.UnlockAsync(a, "x")));
        Assert.Equal(AdminErrors.UserNotFound, Code(await f.Service.ListEventsAsync("x", 0, 20)));
        Assert.Empty(f.Events.All);
    }

    // ── B28 último superadmin ────────────────────────────────────────────────

    [Fact]
    public async Task OwnerSuspendsLeo_Ok_ThenLeoAgainstOwner_LastSuperadmin()
    {
        var f = new AdminFixture();
        var (owner, leo) = await f.SeedSuperadminsAsync();

        var r = await f.Service.SuspendAsync(AdminFixture.ActorFor(owner), leo.Id, null);
        Assert.True(r.IsSuccess, r.Error);
        Assert.Equal(1, await f.Repo.CountActiveSuperadminsAsync());
        Assert.Equal(0, f.Locks.Held);
        Assert.True(f.Locks.Releases >= 1);

        // Leo ya no tiene sesión, pero aunque llamara al servicio:
        var back = await f.Service.SuspendAsync(AdminFixture.ActorFor(leo), owner.Id, null);
        Assert.Equal(AdminErrors.LastSuperadmin, Code(back));
        Assert.Equal(AdminErrors.MsgLastSuperadmin, back.Error);
        Assert.Equal(UserStatuses.Activo, f.Doc(owner.Dni).Status);
        Assert.Equal(1, await f.Repo.CountActiveSuperadminsAsync());
        Assert.Single(f.Events.All);
        Assert.Equal(0, f.Locks.Held);

        // El único activo tampoco se puede suspender a sí mismo.
        Assert.Equal(AdminErrors.CannotActOnSelf, Code(await f.Service.SuspendAsync(AdminFixture.ActorFor(owner), owner.Id, null)));
        Assert.Equal(1, await f.Repo.CountActiveSuperadminsAsync());
    }

    [Fact]
    public async Task CrossedSuspensions_Concurrent_ExactlyOneSucceeds()
    {
        for (var round = 0; round < 25; round++)
        {
            var f = new AdminFixture();
            var (owner, leo) = await f.SeedSuperadminsAsync();

            var results = await Task.WhenAll(
                Task.Run(() => f.Service.SuspendAsync(AdminFixture.ActorFor(owner), leo.Id, null)),
                Task.Run(() => f.Service.SuspendAsync(AdminFixture.ActorFor(leo), owner.Id, null)));

            Assert.Equal(1, results.Count(x => x.IsSuccess));
            var failed = results.Single(x => !x.IsSuccess);
            Assert.Contains(Code(failed), new[] { AdminErrors.LastSuperadmin, AdminErrors.InvalidState });
            Assert.True(await f.Repo.CountActiveSuperadminsAsync() >= 1);
            Assert.Single(f.Events.All);
            Assert.Equal(0, f.Locks.Held);
        }
    }

    [Fact]
    public async Task LockBusy_OperationBusy_NoWrite()
    {
        var f = new AdminFixture();
        var (owner, leo) = await f.SeedSuperadminsAsync();
        f.Locks.Busy = true;

        var r = await f.Service.SuspendAsync(AdminFixture.ActorFor(owner), leo.Id, null);

        Assert.Equal(AdminErrors.OperationBusy, Code(r));
        Assert.Equal(AdminErrors.MsgOperationBusy, r.Error);
        Assert.Equal(UserAdminService.LockAttempts, f.Locks.AcquireAttempts);
        Assert.Equal(UserStatuses.Activo, f.Doc(leo.Dni).Status);
        Assert.Empty(f.Events.All);
    }

    [Fact]
    public async Task SuperadminCanResetAndUnlockOther()
    {
        var f = new AdminFixture();
        var (owner, leo) = await f.SeedSuperadminsAsync();
        Assert.True((await f.Service.ResetPasswordAsync(AdminFixture.ActorFor(owner), leo.Id)).IsSuccess);
    }

    // ── B29 reset ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reset_NewTemp_MustChange_RevokesSessions_OldPasswordFails()
    {
        var f = new AdminFixture();
        var (owner, _) = await f.SeedSuperadminsAsync();
        var acc = await f.SeedAsync(ClientDni, "Ana Pérez");
        var doc0 = f.Doc(ClientDni);
        doc0.FailedLoginCount = 3;
        doc0.LockedUntil = f.Now.AddMinutes(10);
        f.Repo.Seed(doc0);
        var token = TokenFor(acc);
        f.Time.Advance(TimeSpan.FromMinutes(1));

        var r = await f.Service.ResetPasswordAsync(AdminFixture.ActorFor(owner), acc.Id);

        Assert.True(r.IsSuccess, r.Error);
        var doc = f.Doc(ClientDni);
        Assert.True(doc.MustChangePassword);
        Assert.Equal(f.Now, doc.PasswordChangedAt);
        Assert.Null(doc.LockedUntil);
        Assert.Equal(0, doc.FailedLoginCount);
        Assert.True(f.Auth.Hasher.Verify(r.Value!.TemporaryPassword, doc.PasswordHash));
        Assert.False(f.Auth.Hasher.Verify(AdminFixture.Pwd, doc.PasswordHash));
        Assert.Equal(AuthErrors.SessionRevoked, (await f.Auth.Sessions.ValidateAsync(token, CancellationToken.None)).FailureCode);
        Assert.Null(r.Value.User.LockedUntil);
        Assert.True(r.Value.User.MustChangePassword);

        var evt = Assert.Single(f.Events.All);
        Assert.Equal(UserAdminActions.ResetPassword, evt.Action);
        Assert.Empty(evt.Changes);
        Assert.DoesNotContain(r.Value.TemporaryPassword, JsonSerializer.Serialize(evt));
    }

    [Fact]
    public async Task Reset_SuspendedAccount_StaysSuspended()
    {
        var f = new AdminFixture();
        var (owner, _) = await f.SeedSuperadminsAsync();
        var acc = await f.SeedAsync(ClientDni, "Ana Pérez");
        await f.Service.SuspendAsync(AdminFixture.ActorFor(owner), acc.Id, null);

        var r = await f.Service.ResetPasswordAsync(AdminFixture.ActorFor(owner), acc.Id);

        Assert.True(r.IsSuccess);
        Assert.Equal(UserStatuses.Suspendido, f.Doc(ClientDni).Status);
        Assert.Equal(UserStatuses.Suspendido, r.Value!.User.Status);
    }

    [Fact]
    public async Task Reset_Self_CannotActOnSelf()
    {
        var f = new AdminFixture();
        var (owner, _) = await f.SeedSuperadminsAsync();
        var hash = f.Doc(owner.Dni).PasswordHash;

        var r = await f.Service.ResetPasswordAsync(AdminFixture.ActorFor(owner), owner.Id);

        Assert.Equal(AdminErrors.CannotActOnSelf, Code(r));
        Assert.Equal(hash, f.Doc(owner.Dni).PasswordHash);
        Assert.Empty(f.Generator.Issued);
        Assert.Empty(f.Events.All);
    }

    // ── B30 desbloqueo ───────────────────────────────────────────────────────

    [Fact]
    public async Task Unlock_ActiveLock_ClearsAndAudits()
    {
        var f = new AdminFixture();
        var (owner, _) = await f.SeedSuperadminsAsync();
        var acc = await f.SeedAsync(ClientDni, "Ana Pérez");
        for (var i = 0; i < f.Auth.Options.MaxFailedAttempts; i++)
            await f.Auth.Provider.AuthenticateAsync(ClientDni, null, "equivocada-123");
        Assert.NotNull(f.Doc(ClientDni).LockedUntil);
        var list = await f.Service.ListAsync();
        Assert.NotNull(list.Value!.Users.Single(u => u.Dni == ClientDni).LockedUntil);

        var r = await f.Service.UnlockAsync(AdminFixture.ActorFor(owner), acc.Id);

        Assert.True(r.IsSuccess, r.Error);
        var doc = f.Doc(ClientDni);
        Assert.Equal(0, doc.FailedLoginCount);
        Assert.Null(doc.LockedUntil);
        Assert.Null(r.Value!.User.LockedUntil);
        Assert.Equal(UserAdminActions.Unlock, Assert.Single(f.Events.All).Action);
        Assert.True((await f.Auth.Provider.AuthenticateAsync(ClientDni, null, AdminFixture.Pwd)).IsSuccess);
    }

    [Fact]
    public async Task Unlock_NoLockOrExpired_InvalidState()
    {
        var f = new AdminFixture();
        var (owner, _) = await f.SeedSuperadminsAsync();
        var acc = await f.SeedAsync(ClientDni, "Ana Pérez");

        var none = await f.Service.UnlockAsync(AdminFixture.ActorFor(owner), acc.Id);
        Assert.Equal(AdminErrors.InvalidState, Code(none));
        Assert.Equal(AdminErrors.MsgNotLocked, none.Error);

        var doc = f.Doc(ClientDni);
        doc.LockedUntil = f.Now.AddMinutes(-1);
        f.Repo.Seed(doc);
        Assert.Equal(AdminErrors.InvalidState, Code(await f.Service.UnlockAsync(AdminFixture.ActorFor(owner), acc.Id)));
        // Vencido: el DTO no lo muestra como bloqueado.
        Assert.Null((await f.Service.GetAsync(acc.Id)).Value!.User.LockedUntil);
        Assert.Empty(f.Events.All);
    }

    [Fact]
    public async Task Unlock_Self_IsAllowed()
    {
        var f = new AdminFixture();
        var (owner, _) = await f.SeedSuperadminsAsync();
        var doc = f.Doc(owner.Dni);
        doc.LockedUntil = f.Now.AddMinutes(5);
        f.Repo.Seed(doc);
        Assert.True((await f.Service.UnlockAsync(AdminFixture.ActorFor(owner), owner.Id)).IsSuccess);
    }

    // ── B31 auditoría ────────────────────────────────────────────────────────

    [Fact]
    public async Task AuditFailsTwice_ActionApplied_SuccessAndLogErrorWithoutSecrets()
    {
        var f = new AdminFixture();
        var (owner, _) = await f.SeedSuperadminsAsync();
        var acc = await f.SeedAsync(ClientDni, "Ana Pérez");
        f.Events.FailNextInserts = 2;

        var r = await f.Service.ResetPasswordAsync(AdminFixture.ActorFor(owner), acc.Id);

        Assert.True(r.IsSuccess);
        Assert.Equal(2, f.Events.InsertAttempts);
        Assert.Empty(f.Events.All);
        Assert.True(f.Auth.Hasher.Verify(r.Value!.TemporaryPassword, f.Doc(ClientDni).PasswordHash));
        var err = Assert.Single(f.Log.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains("Auditoría no registrada", err.Message);
        Assert.Contains(acc.Id, err.Message);
        Assert.DoesNotContain(r.Value.TemporaryPassword, err.Message);
        Assert.DoesNotContain("pbkdf2", err.Message);
    }

    [Fact]
    public async Task AuditFailsOnce_RetriedAndWritten()
    {
        var f = new AdminFixture();
        var (owner, _) = await f.SeedSuperadminsAsync();
        var acc = await f.SeedAsync(ClientDni, "Ana Pérez");
        f.Events.FailNextInserts = 1;

        var r = await f.Service.SuspendAsync(AdminFixture.ActorFor(owner), acc.Id, null);

        Assert.True(r.IsSuccess);
        Assert.Single(f.Events.All);
        Assert.DoesNotContain(f.Log.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task ListEvents_DescendingPagedWithHasMore_ClampsRange()
    {
        var f = new AdminFixture();
        var (owner, _) = await f.SeedSuperadminsAsync();
        var acc = await f.SeedAsync(ClientDni, "Ana Pérez");
        var a = AdminFixture.ActorFor(owner);
        for (var i = 0; i < 5; i++)
        {
            f.Time.Advance(TimeSpan.FromSeconds(1));
            Assert.True((await f.Service.SuspendAsync(a, acc.Id, new AdminSuspendRequest($"m{i}"))).IsSuccess);
            f.Time.Advance(TimeSpan.FromSeconds(1));
            Assert.True((await f.Service.ReactivateAsync(a, acc.Id)).IsSuccess);
        }

        var p1 = (await f.Service.ListEventsAsync(acc.Id, 0, 4)).Value!;
        Assert.Equal(4, p1.Events.Count);
        Assert.True(p1.HasMore);
        Assert.Equal(UserAdminActions.Reactivate, p1.Events[0].Action);
        Assert.True(p1.Events.Zip(p1.Events.Skip(1)).All(p => p.First.At >= p.Second.At));

        var p3 = (await f.Service.ListEventsAsync(acc.Id, 8, 4)).Value!;
        Assert.Equal(2, p3.Events.Count);
        Assert.False(p3.HasMore);
        Assert.Equal("m0", p3.Events[1].Reason);

        // Fuera de rango se ajusta: offset -3 → 0, limit 0 → 1, limit 999 → 50.
        var clampedLow = (await f.Service.ListEventsAsync(acc.Id, -3, 0)).Value!;
        Assert.Single(clampedLow.Events);
        Assert.True(clampedLow.HasMore);
        var clampedHigh = (await f.Service.ListEventsAsync(acc.Id, 0, 999)).Value!;
        Assert.Equal(10, clampedHigh.Events.Count);
        Assert.False(clampedHigh.HasMore);
        Assert.Equal("Dueño", clampedHigh.Events[0].ActorName);
        Assert.Equal(owner.Dni, clampedHigh.Events[0].ActorDni);
    }

    [Fact]
    public void EventRepository_HasNoUpdateOrDelete()
    {
        var names = typeof(Factum.Backend.Infrastructure.IUserAdminEventRepository).GetMethods()
            .Select(m => m.Name).OrderBy(n => n).ToArray();
        Assert.Equal(["EnsureIndexesAsync", "InsertAsync", "ListByTargetAsync"], names);
    }

    // ── listado / detalle ────────────────────────────────────────────────────

    [Fact]
    public async Task List_SortedByName_WithCountsAndNames_NoHashes()
    {
        var f = new AdminFixture();
        var (owner, _) = await f.SeedSuperadminsAsync();
        await f.Service.CreateAsync(AdminFixture.ActorFor(owner), CreateReq(name: "Ana Pérez"));
        f.Cases.Counts[ClientDni] = 7;
        f.Cases.Counts["99999999"] = 3; // DNI sin cuenta: no aparece
        var callsBefore = f.Cases.CountCalls;

        var r = await f.Service.ListAsync();

        Assert.True(r.IsSuccess);
        var users = r.Value!.Users;
        Assert.Equal(["Ana Pérez", "Dueño", "Leo"], users.Select(u => u.Name));
        var ana = users[0];
        Assert.Equal(7, ana.CaseCount);
        Assert.Equal("Dueño", ana.CreatedByName);
        Assert.Equal("bootstrap", users[1].CreatedBy);
        Assert.Null(users[1].CreatedByName);
        Assert.Equal(0, users[1].CaseCount);
        Assert.Equal(callsBefore + 1, f.Cases.CountCalls); // una sola agregación para todo el listado
        var json = JsonSerializer.Serialize(r.Value);
        Assert.DoesNotContain("pbkdf2", json);
    }

    [Fact]
    public async Task Get_ResolvesNamesAndCount()
    {
        var f = new AdminFixture();
        var (owner, leo) = await f.SeedSuperadminsAsync();
        var created = await f.Service.CreateAsync(AdminFixture.ActorFor(owner), CreateReq());
        await f.Service.SuspendAsync(AdminFixture.ActorFor(leo), created.Value!.User.Id, new AdminSuspendRequest("x"));
        f.Cases.Counts[ClientDni] = 2;

        var dto = (await f.Service.GetAsync(created.Value.User.Id)).Value!.User;
        Assert.Equal("Dueño", dto.CreatedByName);
        Assert.Equal("Leo", dto.SuspendedByName);
        Assert.Equal(2, dto.CaseCount);
        Assert.Equal("Cliente nuevo\nPaga por transferencia", dto.Notes);
    }

    // ── B34 modo ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(AuthModes.Dev)]
    [InlineData(AuthModes.External)]
    public async Task NotLocal_EveryMethod_NotAvailable_TouchesNothing(string mode)
    {
        var f = new AdminFixture(new AuthSettings(mode, null, null, [], []));
        f.Repo.ThrowOnUse = true;
        f.Events.ThrowOnUse = true;
        f.Locks.ThrowOnUse = true;
        f.Cases.ThrowOnUse = true;
        f.Generator.ThrowOnUse = true;
        var a = new AdminActor("20111111", "Dueño", null);

        Assert.Equal(AdminErrors.NotAvailable, Code(await f.Service.ListAsync()));
        Assert.Equal(AdminErrors.NotAvailable, Code(await f.Service.GetAsync("x")));
        Assert.Equal(AdminErrors.NotAvailable, Code(await f.Service.CreateAsync(a, CreateReq())));
        Assert.Equal(AdminErrors.NotAvailable, Code(await f.Service.UpdateAsync(a, "x",
            new AdminUpdateUserRequest("A", null, null, null, null, null, DateTime.UtcNow))));
        Assert.Equal(AdminErrors.NotAvailable, Code(await f.Service.SuspendAsync(a, "x", null)));
        Assert.Equal(AdminErrors.NotAvailable, Code(await f.Service.ReactivateAsync(a, "x")));
        Assert.Equal(AdminErrors.NotAvailable, Code(await f.Service.ResetPasswordAsync(a, "x")));
        Assert.Equal(AdminErrors.NotAvailable, Code(await f.Service.UnlockAsync(a, "x")));
        Assert.Equal(AdminErrors.NotAvailable, Code(await f.Service.ListEventsAsync("x", 0, 20)));
        Assert.Equal(0, f.Repo.Calls);
    }

    // ── regresión #6 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task EmergencyReset_ClearsSuspensionReason()
    {
        var f = new AdminFixture();
        var (owner, leo) = await f.SeedSuperadminsAsync();
        await f.Service.SuspendAsync(AdminFixture.ActorFor(owner), leo.Id, new AdminSuspendRequest("vacaciones"));
        Assert.Equal("vacaciones", f.Doc(leo.Dni).SuspensionReason);

        Assert.True(await f.Repo.ApplyEmergencyResetAsync(leo.Id, "h", "m", f.Now));
        Assert.Null(f.Doc(leo.Dni).SuspensionReason);
        Assert.Equal(UserStatuses.Activo, f.Doc(leo.Dni).Status);
    }
}
