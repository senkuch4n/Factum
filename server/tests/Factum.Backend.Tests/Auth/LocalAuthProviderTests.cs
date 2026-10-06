using Factum.Backend.Models;
using Factum.Backend.Services.Auth;

namespace Factum.Backend.Tests.Auth;

/// <summary>usuarios-locales §6.3 (B29).</summary>
public sealed class LocalAuthProviderTests
{
    private const string Dni = "30111222";
    private const string Pwd = "temporal-123";

    private static string? Code<T>(Factum.Backend.Common.Result<T> r) => AuthErrors.CodeOf(r);

    [Fact]
    public async Task Login_Ok_ResetsCounterAndWritesLastLogin()
    {
        var f = new LocalAuthFixture();
        var acc = await f.CreateUserAsync(mustChange: false);
        acc.FailedLoginCount = 3;
        f.Repo.Seed(acc);

        var r = await f.Provider.AuthenticateAsync(Dni, null, Pwd);

        Assert.True(r.IsSuccess);
        Assert.Equal(Dni, r.Value!.User.Dni);
        Assert.Equal("Ana Pérez", r.Value.User.Name);
        Assert.Equal("AP", r.Value.User.Sigla);
        Assert.Equal(UserRoles.Cliente, r.Value.Role);
        Assert.False(r.Value.MustChangePassword);
        Assert.Equal(acc.PasswordChangedAt, r.Value.PasswordChangedAt);
        var saved = f.Repo.Get(Dni)!;
        Assert.Equal(0, saved.FailedLoginCount);
        Assert.Null(saved.LockedUntil);
        Assert.Equal(f.Now, saved.LastLoginAt);
    }

    [Fact]
    public async Task Login_IgnoresUsername()
    {
        var f = new LocalAuthFixture();
        await f.CreateUserAsync();
        Assert.True((await f.Provider.AuthenticateAsync(Dni, "cualquier.cosa", Pwd)).IsSuccess);
        Assert.True((await f.Provider.AuthenticateAsync(Dni, "", Pwd)).IsSuccess);
    }

    [Fact]
    public async Task UnknownDni_And_WrongPassword_SameErrorAndOneVerifyEach()
    {
        var f = new LocalAuthFixture();
        await f.CreateUserAsync();

        f.Hasher.Reset();
        var unknown = await f.Provider.AuthenticateAsync("40999888", null, Pwd);
        Assert.Equal(1, f.Hasher.VerifyCalls);

        f.Hasher.Reset();
        var wrong = await f.Provider.AuthenticateAsync(Dni, null, "otra-contraseña");
        Assert.Equal(1, f.Hasher.VerifyCalls);

        Assert.False(unknown.IsSuccess);
        Assert.False(wrong.IsSuccess);
        Assert.Equal("invalid_credentials", Code(unknown));
        Assert.Equal(Code(unknown), Code(wrong));
        Assert.Equal("DNI o contraseña incorrectos.", unknown.Error);
        Assert.Equal(unknown.Error, wrong.Error);
    }

    [Theory]
    [InlineData("123456", Pwd)]
    [InlineData("abcdefgh", Pwd)]
    [InlineData(Dni, "")]
    public async Task InvalidShape_InvalidCredentials_WithOneVerify(string dni, string pwd)
    {
        var f = new LocalAuthFixture();
        await f.CreateUserAsync();
        var r = await f.Provider.AuthenticateAsync(dni, null, pwd);
        Assert.Equal("invalid_credentials", Code(r));
        Assert.Equal(1, f.Hasher.VerifyCalls);
    }

    [Fact]
    public async Task PasswordOver128_InvalidCredentials()
    {
        var f = new LocalAuthFixture();
        await f.CreateUserAsync();
        var r = await f.Provider.AuthenticateAsync(Dni, null, new string('a', 129));
        Assert.Equal("invalid_credentials", Code(r));
        Assert.Equal(0, f.Repo.Get(Dni)!.FailedLoginCount);
    }

    [Fact]
    public async Task FourFailures_Invalid_FifthLocks()
    {
        var f = new LocalAuthFixture();
        await f.CreateUserAsync();

        for (var i = 1; i <= 4; i++)
        {
            var r = await f.Provider.AuthenticateAsync(Dni, null, "incorrecta-" + i);
            Assert.Equal("invalid_credentials", Code(r));
            Assert.Equal(i, f.Repo.Get(Dni)!.FailedLoginCount);
        }

        var fifth = await f.Provider.AuthenticateAsync(Dni, null, "incorrecta-5");
        Assert.Equal("account_locked", Code(fifth));
        Assert.Equal("Demasiados intentos fallidos. Probá de nuevo en 15 minutos.", fifth.Error);
        var saved = f.Repo.Get(Dni)!;
        Assert.Equal(0, saved.FailedLoginCount);
        Assert.Equal(f.Now.AddMinutes(15), saved.LockedUntil);
        Assert.Contains(f.ProviderLog.Messages, m => m.Contains("Cuenta bloqueada por intentos fallidos"));
        Assert.DoesNotContain(f.ProviderLog.Messages, m => m.Contains(Dni));
    }

    [Fact]
    public async Task WhileLocked_CorrectPassword_Locked_WithoutVerify_ThenUnlocksAfterLockout()
    {
        var f = new LocalAuthFixture();
        await f.CreateUserAsync();
        for (var i = 0; i < 5; i++) await f.Provider.AuthenticateAsync(Dni, null, "incorrecta");

        f.Hasher.Reset();
        var during = await f.Provider.AuthenticateAsync(Dni, null, Pwd);
        Assert.Equal("account_locked", Code(during));
        Assert.Equal(0, f.Hasher.VerifyCalls);

        f.Time.Advance(TimeSpan.FromMinutes(14));
        Assert.Equal("account_locked", Code(await f.Provider.AuthenticateAsync(Dni, null, Pwd)));

        f.Time.Advance(TimeSpan.FromMinutes(1).Add(TimeSpan.FromSeconds(1)));
        var after = await f.Provider.AuthenticateAsync(Dni, null, Pwd);
        Assert.True(after.IsSuccess);
        Assert.Null(f.Repo.Get(Dni)!.LockedUntil);
    }

    [Fact]
    public async Task LockoutMinutes_FromOptions()
    {
        var f = new LocalAuthFixture(new LocalAuthOptions { MaxFailedAttempts = 2, LockoutMinutes = 3 });
        await f.CreateUserAsync();
        Assert.Equal("invalid_credentials", Code(await f.Provider.AuthenticateAsync(Dni, null, "mal-1")));
        var r = await f.Provider.AuthenticateAsync(Dni, null, "mal-2");
        Assert.Equal("account_locked", Code(r));
        Assert.Equal("Demasiados intentos fallidos. Probá de nuevo en 3 minutos.", r.Error);
    }

    [Fact]
    public async Task Suspended_CorrectPassword_AccountSuspended_NoCounters()
    {
        var f = new LocalAuthFixture();
        await f.CreateUserAsync(status: UserStatuses.Suspendido);
        var r = await f.Provider.AuthenticateAsync(Dni, null, Pwd);
        Assert.Equal("account_suspended", Code(r));
        Assert.Equal("Tu cuenta está suspendida. Comunicate con Factum para reactivarla.", r.Error);
        var saved = f.Repo.Get(Dni)!;
        Assert.Null(saved.LastLoginAt);
        Assert.Equal(0, saved.FailedLoginCount);
    }

    [Fact]
    public async Task Suspended_WrongPassword_InvalidCredentials()
    {
        var f = new LocalAuthFixture();
        await f.CreateUserAsync(status: UserStatuses.Suspendido);
        var r = await f.Provider.AuthenticateAsync(Dni, null, "incorrecta");
        Assert.Equal("invalid_credentials", Code(r));
    }

    [Fact]
    public async Task MustChangePassword_SuccessWithFlag()
    {
        var f = new LocalAuthFixture();
        await f.CreateUserAsync(role: UserRoles.Superadmin, mustChange: true);
        var r = await f.Provider.AuthenticateAsync(Dni, null, Pwd);
        Assert.True(r.IsSuccess);
        Assert.True(r.Value!.MustChangePassword);
        Assert.Equal(UserRoles.Superadmin, r.Value.Role);
    }

    [Fact]
    public async Task OldHash_IsRehashed_WithoutChangingPasswordChangedAt()
    {
        var f = new LocalAuthFixture();
        var acc = await f.CreateUserAsync();
        acc.PasswordHash = new Pbkdf2PasswordHasher(10).Hash(Pwd);
        f.Repo.Seed(acc);

        Assert.True((await f.Provider.AuthenticateAsync(Dni, null, Pwd)).IsSuccess);
        var saved = f.Repo.Get(Dni)!;
        Assert.NotEqual(acc.PasswordHash, saved.PasswordHash);
        Assert.StartsWith("pbkdf2-sha256$1000$", saved.PasswordHash);
        Assert.Equal(acc.PasswordChangedAt, saved.PasswordChangedAt);
    }

    [Fact]
    public async Task Logs_NeverContainDniOrPassword()
    {
        var f = new LocalAuthFixture();
        await f.CreateUserAsync();
        await f.Provider.AuthenticateAsync(Dni, null, "incorrecta-xyz");
        await f.Provider.AuthenticateAsync("40999888", null, Pwd);
        await f.Provider.AuthenticateAsync(Dni, null, Pwd);
        foreach (var m in f.ProviderLog.Messages)
        {
            Assert.DoesNotContain(Dni, m);
            Assert.DoesNotContain("40999888", m);
            Assert.DoesNotContain(Pwd, m);
            Assert.DoesNotContain("incorrecta-xyz", m);
        }
    }
}
