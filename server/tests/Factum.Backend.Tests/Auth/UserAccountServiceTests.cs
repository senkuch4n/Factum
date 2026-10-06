using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Auth;

namespace Factum.Backend.Tests.Auth;

/// <summary>usuarios-locales §5.3 y §6.6 (B32).</summary>
public sealed class UserAccountServiceTests
{
    private const string Dni = "30111222";
    private const string Temp = "temporal-123";
    private const string NewPwd = "una-nueva-clave-segura";

    private static ChangePasswordRequest Req(string current, string next, string? confirmation = null) =>
        new(current, next, confirmation ?? next);

    private static (string? Code, string? Field) CodeField<T>(Result<T> r) =>
        (AuthErrors.CodeOf(r), r.Details is { } d && d.TryGetValue("field", out var f) ? f as string : null);

    [Fact]
    public async Task Change_Ok_ClearsFlag_NewPca_TokenMatchesDatabase()
    {
        var f = new LocalAuthFixture();
        var before = await f.CreateUserAsync(role: UserRoles.Superadmin);
        f.Time.Advance(TimeSpan.FromMinutes(3).Add(TimeSpan.FromTicks(4321)));

        var r = await f.Accounts.ChangeOwnPasswordAsync(Dni, Req(Temp, NewPwd));

        Assert.True(r.IsSuccess, r.Error);
        Assert.Equal(new UserDto(Dni, "Ana Pérez", "AP", UserRoles.Superadmin, false), r.Value!.User);
        var saved = f.Repo.Get(Dni)!;
        Assert.False(saved.MustChangePassword);
        Assert.NotEqual(before.PasswordChangedAt, saved.PasswordChangedAt);
        Assert.Equal(AuthTime.TruncateToMs(f.Now), saved.PasswordChangedAt);
        Assert.Equal(0, saved.PasswordChangedAt.Ticks % TimeSpan.TicksPerMillisecond);
        Assert.Equal(AuthTime.ToUnixMs(saved.PasswordChangedAt), f.Auth.ValidateToken(r.Value.Token)!.PasswordChangedAtMs);
        Assert.Equal(0, saved.FailedLoginCount);
        Assert.Null(saved.LockedUntil);

        // El token nuevo pasa el validador.
        var check = await f.Sessions.ValidateAsync(f.Auth.ValidateToken(r.Value.Token)!, CancellationToken.None);
        Assert.True(check.Ok);
        Assert.False(check.Session!.MustChangePassword);
    }

    [Fact]
    public async Task Change_OldTokenNoLongerPasses_AndTemporaryNoLongerVerifies()
    {
        var f = new LocalAuthFixture();
        await f.CreateUserAsync();
        var login = await f.Auth.LoginAsync(new LoginRequest(Dni, Temp));
        var oldPayload = f.Auth.ValidateToken(login.Value!.Token)!;
        Assert.True((await f.Sessions.ValidateAsync(oldPayload, CancellationToken.None)).Ok);

        f.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.True((await f.Accounts.ChangeOwnPasswordAsync(Dni, Req(Temp, NewPwd))).IsSuccess);

        var check = await f.Sessions.ValidateAsync(oldPayload, CancellationToken.None);
        Assert.False(check.Ok);
        Assert.Equal("session_revoked", check.FailureCode);

        var saved = f.Repo.Get(Dni)!;
        Assert.False(f.Hasher.Verify(Temp, saved.PasswordHash));
        Assert.True(f.Hasher.Verify(NewPwd, saved.PasswordHash));
        Assert.Equal("invalid_credentials", AuthErrors.CodeOf(await f.Provider.AuthenticateAsync(Dni, null, Temp)));
        Assert.True((await f.Provider.AuthenticateAsync(Dni, null, NewPwd)).IsSuccess);
    }

    [Fact]
    public async Task WrongCurrent_InvalidCurrentPassword_CounterUp_LocksAtLimit()
    {
        var f = new LocalAuthFixture();
        await f.CreateUserAsync();

        for (var i = 1; i <= 4; i++)
        {
            var r = await f.Accounts.ChangeOwnPasswordAsync(Dni, Req("incorrecta-" + i, NewPwd));
            Assert.Equal(("invalid_current_password", "current_password"), CodeField(r));
            Assert.Equal("La contraseña actual no es correcta.", r.Error);
            Assert.Equal(i, f.Repo.Get(Dni)!.FailedLoginCount);
        }

        var fifth = await f.Accounts.ChangeOwnPasswordAsync(Dni, Req("incorrecta-5", NewPwd));
        Assert.Equal("account_locked", AuthErrors.CodeOf(fifth));
        Assert.Equal("Demasiados intentos fallidos. Probá de nuevo en 15 minutos.", fifth.Error);
        Assert.NotNull(f.Repo.Get(Dni)!.LockedUntil);

        // Bloqueada: ni con la actual correcta.
        f.Hasher.Reset();
        Assert.Equal("account_locked", AuthErrors.CodeOf(await f.Accounts.ChangeOwnPasswordAsync(Dni, Req(Temp, NewPwd))));
        Assert.Equal(0, f.Hasher.VerifyCalls);
    }

    [Theory]
    [InlineData(NewPwd, "otra-cosa-distinta", "password_mismatch", "new_password_confirmation")]
    [InlineData("corta", null, "password_too_short", "new_password")]
    [InlineData("x30111222yz", null, "password_contains_dni", "new_password")]
    public async Task PolicyRules_CodeAndField_DoNotCount(string next, string? confirmation, string code, string field)
    {
        var f = new LocalAuthFixture();
        await f.CreateUserAsync();

        // Aun con la actual incorrecta, la regla de forma gana y no cuenta como intento.
        var r = await f.Accounts.ChangeOwnPasswordAsync(Dni, Req("incorrecta", next, confirmation));

        Assert.Equal((code, field), CodeField(r));
        Assert.Equal(0, f.Repo.Get(Dni)!.FailedLoginCount);
        Assert.Equal(0, f.Hasher.VerifyCalls);
    }

    [Fact]
    public async Task TooLong_CodeAndField()
    {
        var f = new LocalAuthFixture();
        await f.CreateUserAsync();
        var r = await f.Accounts.ChangeOwnPasswordAsync(Dni, Req(Temp, new string('a', 129)));
        Assert.Equal(("password_too_long", "new_password"), CodeField(r));
        Assert.Equal(0, f.Repo.Get(Dni)!.FailedLoginCount);
    }

    [Fact]
    public async Task SameAsCurrent()
    {
        var f = new LocalAuthFixture();
        await f.CreateUserAsync(password: "misma-clave-123");
        var r = await f.Accounts.ChangeOwnPasswordAsync(Dni, Req("misma-clave-123", "misma-clave-123"));
        Assert.Equal(("password_same_as_current", "new_password"), CodeField(r));
        Assert.True(f.Repo.Get(Dni)!.MustChangePassword);
    }

    [Fact]
    public async Task NotLocal_NotAvailable()
    {
        var f = new LocalAuthFixture();
        var svc = new UserAccountService(new AuthSettings(AuthModes.Dev, null, null, [], []), f.Repo, f.Hasher, f.Auth, f.Time);
        var r = await svc.ChangeOwnPasswordAsync(Dni, Req(Temp, NewPwd));
        Assert.Equal("not_available", AuthErrors.CodeOf(r));
    }

    [Fact]
    public async Task Create_ExistingDni_Conflict()
    {
        var f = new LocalAuthFixture();
        await f.CreateUserAsync();
        var r = await f.Accounts.CreateAsync(new NewUserAccount(Dni, "Otra", "", UserRoles.Cliente, "temporal-456"), "test");
        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorKind.Conflict, r.Kind);
        Assert.Single(f.Repo.All);
    }

    [Fact]
    public async Task Create_SetsDefaults()
    {
        var f = new LocalAuthFixture();
        var r = await f.Accounts.CreateAsync(new NewUserAccount(" 9900001 ", "  Nuevo  ", " NV ", UserRoles.Cliente, "temporal-456"), "20333444");
        Assert.True(r.IsSuccess, r.Error);
        var acc = f.Repo.Get("9900001")!;
        Assert.Equal("Nuevo", acc.Name);
        Assert.Equal("NV", acc.Sigla);
        Assert.Equal(UserStatuses.Activo, acc.Status);
        Assert.True(acc.MustChangePassword);
        Assert.Equal("20333444", acc.CreatedBy);
        Assert.Equal(acc.CreatedAt, acc.PasswordChangedAt);
        Assert.Equal(acc.CreatedAt, acc.UpdatedAt);
        Assert.True(Guid.TryParse(acc.Id, out _));
        Assert.True(f.Hasher.Verify("temporal-456", acc.PasswordHash));
    }

    [Theory]
    [InlineData("123456", "Nombre", UserRoles.Cliente, "temporal-456")]
    [InlineData("30111222", "  ", UserRoles.Cliente, "temporal-456")]
    [InlineData("30111222", "Nombre", "admin", "temporal-456")]
    [InlineData("30111222", "Nombre", UserRoles.Cliente, "corta")]
    public async Task Create_Invalid(string dni, string name, string role, string temp)
    {
        var f = new LocalAuthFixture();
        var r = await f.Accounts.CreateAsync(new NewUserAccount(dni, name, "", role, temp), "test");
        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorKind.Validation, r.Kind);
        Assert.DoesNotContain(temp, r.Error!.Replace("contraseña temporal", ""));
        Assert.Empty(f.Repo.All);
    }
}
