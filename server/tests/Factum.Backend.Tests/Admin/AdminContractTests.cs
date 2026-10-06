using System.Text.Json;
using Factum.Backend.Controllers;
using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Admin;
using Factum.Backend.Services.Auth;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Factum.Backend.Tests.Admin;

/// <summary>abm-clientes contrato §8.1 (B32), filtros y mapeo de códigos (B33), lista blanca (§6.5).</summary>
public sealed class AdminContractTests
{
    private static readonly JsonSerializerOptions Snake = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static AdminUserDto SampleDto() => new(
        "id-1", "30111222", "Ana", "AP", UserRoles.Cliente, UserStatuses.Activo, true, null, null,
        DateTime.UtcNow, "20111111", "Dueño", DateTime.UtcNow, null, null, null, null,
        "", "", "", "", 3);

    private static string[] Keys(string json) =>
        JsonDocument.Parse(json).RootElement.EnumerateObject().Select(p => p.Name).ToArray();

    [Fact]
    public void AdminUserDto_KeysMatchContract_NoSecrets()
    {
        var json = JsonSerializer.Serialize(SampleDto(), Snake);
        Assert.Equal(
        [
            "id", "dni", "name", "sigla", "role", "status", "must_change_password", "locked_until", "last_login_at",
            "created_at", "created_by", "created_by_name", "updated_at", "suspended_at", "suspended_by",
            "suspended_by_name", "suspension_reason", "contact_phone", "contact_email", "organization", "notes",
            "case_count",
        ], Keys(json));
        Assert.DoesNotContain("password_hash", json);
        Assert.DoesNotContain("last_emergency_reset_hash", json);
        Assert.DoesNotContain("temporary_password", json);
    }

    [Fact]
    public void Responses_KeysMatchContract()
    {
        var dto = SampleDto();
        Assert.Equal(["users"], Keys(JsonSerializer.Serialize(new AdminUserListResponse([dto]), Snake)));
        Assert.Equal(["user"], Keys(JsonSerializer.Serialize(new AdminUserResponse(dto), Snake)));
        Assert.Equal(["user", "temporary_password"],
            Keys(JsonSerializer.Serialize(new AdminUserWithPasswordResponse(dto, "abcd-efgh-jkmn"), Snake)));
        Assert.Equal(["user", "changed"], Keys(JsonSerializer.Serialize(new AdminUpdateUserResponse(dto, true), Snake)));
        var evt = new AdminUserEventDto("e1", DateTime.UtcNow, "20111111", "Dueño", UserAdminActions.Update,
            [new AdminUserChangeDto("name", "a", "b")], null, "1.2.3.4");
        var events = JsonSerializer.Serialize(new AdminUserEventsResponse([evt], true), Snake);
        Assert.Equal(["events", "has_more"], Keys(events));
        var e0 = JsonDocument.Parse(events).RootElement.GetProperty("events")[0];
        Assert.Equal(["id", "at", "actor_dni", "actor_name", "action", "changes", "reason", "ip"],
            e0.EnumerateObject().Select(p => p.Name));
        Assert.Equal(["field", "from", "to"], e0.GetProperty("changes")[0].EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void Requests_BindFromSnakeCase()
    {
        var create = JsonSerializer.Deserialize<AdminCreateUserRequest>(
            """{"dni":"30111222","name":"Ana","sigla":"AP","contact_phone":"123456","contact_email":"a@b.co","organization":"O","notes":"N","role":"superadmin"}""",
            Snake)!;
        Assert.Equal(new AdminCreateUserRequest("30111222", "Ana", "AP", "123456", "a@b.co", "O", "N"), create);

        var update = JsonSerializer.Deserialize<AdminUpdateUserRequest>(
            """{"name":"Ana","sigla":"","contact_phone":"","contact_email":"","organization":"","notes":"","expected_updated_at":"2026-10-06T12:00:00.123Z"}""",
            Snake)!;
        Assert.Equal(new DateTime(2026, 10, 6, 12, 0, 0, 123, DateTimeKind.Utc), update.ExpectedUpdatedAt);
        Assert.Equal(DateTimeKind.Utc, update.ExpectedUpdatedAt!.Value.Kind);

        Assert.Equal("x", JsonSerializer.Deserialize<AdminSuspendRequest>("""{"reason":"x"}""", Snake)!.Reason);
    }

    [Fact]
    public void WithPasswordResponse_ToString_HidesTemporary()
    {
        var s = new AdminUserWithPasswordResponse(SampleDto(), "abcd-efgh-jkmn").ToString();
        Assert.DoesNotContain("abcd-efgh-jkmn", s);
        Assert.Contains("id-1", s);
    }

    [Theory]
    [InlineData(AuthModes.Local, true)]
    [InlineData(AuthModes.Dev, false)]
    [InlineData(AuthModes.External, false)]
    public void RequireLocalAuthMode_IsAllowed(string mode, bool allowed) =>
        Assert.Equal(allowed, RequireLocalAuthModeAttribute.IsAllowed(new AuthSettings(mode, null, null, [], [])));

    [Fact]
    public void RequireLocalAuthMode_RunsBeforeRequireSuperadmin()
    {
        IOrderedFilter local = new RequireLocalAuthModeAttribute();
        Assert.True(local.Order < 0);
        Assert.False(typeof(IOrderedFilter).IsAssignableFrom(typeof(RequireSuperadminAttribute)));
    }

    [Theory]
    [InlineData(AdminErrors.ValidationFailed, 400)]
    [InlineData(AdminErrors.UserNotFound, 404)]
    [InlineData(AdminErrors.NotAvailable, 404)]
    [InlineData(AdminErrors.DniTaken, 409)]
    [InlineData(AdminErrors.StaleUpdate, 409)]
    [InlineData(AdminErrors.CannotActOnSelf, 409)]
    [InlineData(AdminErrors.LastSuperadmin, 409)]
    [InlineData(AdminErrors.InvalidState, 409)]
    [InlineData(AdminErrors.OperationBusy, 409)]
    public void StatusFor_MapsCodes(string code, int status) =>
        Assert.Equal(status, AdminUsersController.StatusFor(code));

    [Fact]
    public void ErrorCodes_MatchContractStrings()
    {
        Assert.Equal(
            ["validation_failed", "user_not_found", "dni_taken", "stale_update", "cannot_act_on_self",
             "last_superadmin", "invalid_state", "operation_busy", "not_available"],
            new[] { AdminErrors.ValidationFailed, AdminErrors.UserNotFound, AdminErrors.DniTaken, AdminErrors.StaleUpdate,
                AdminErrors.CannotActOnSelf, AdminErrors.LastSuperadmin, AdminErrors.InvalidState,
                AdminErrors.OperationBusy, AdminErrors.NotAvailable });
        Assert.Equal(["create", "update", "suspend", "reactivate", "reset_password", "unlock"],
            new[] { UserAdminActions.Create, UserAdminActions.Update, UserAdminActions.Suspend,
                UserAdminActions.Reactivate, UserAdminActions.ResetPassword, UserAdminActions.Unlock });
    }

    [Fact]
    public void AdminChanges_WhitelistNeverIncludesPasswords()
    {
        Assert.DoesNotContain(AdminChanges.AllowedFields, f => f.Contains("password") || f.Contains("hash"));
        var acc = new UserAccount
        {
            Dni = "30111222", Name = "Ana", Role = UserRoles.Cliente,
            PasswordHash = "pbkdf2-sha256$1000$sal$hash", LastEmergencyResetHash = "marca",
        };
        var create = AdminChanges.ForCreate(acc);
        Assert.All(create, c => Assert.Contains(c.Field, AdminChanges.AllowedFields));
        Assert.DoesNotContain(create, c => (c.To ?? "").Contains("pbkdf2") || c.To == "marca");

        var update = AdminChanges.ForUpdate(acc,
            new Factum.Backend.Infrastructure.AccountEditableFields("Ana", "", "", "", "", ""));
        Assert.Empty(update);
    }
}
