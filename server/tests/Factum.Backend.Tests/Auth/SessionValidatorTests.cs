using Factum.Backend.Models;
using Factum.Backend.Services.Auth;

namespace Factum.Backend.Tests.Auth;

/// <summary>usuarios-locales §6.4 (B30).</summary>
public sealed class SessionValidatorTests
{
    private const string Dni = "30111222";

    private static TokenPayload Payload(long? pca, string name = "Nombre del token") =>
        new(new User { Dni = Dni, Name = name, Sigla = "TK" }, pca);

    [Theory]
    [InlineData(AuthModes.Dev)]
    [InlineData(AuthModes.External)]
    public async Task DevOrExternal_DoesNotQueryRepository(string mode)
    {
        var repo = new InMemoryUserRepository { ThrowOnUse = true };
        var v = new SessionValidator(new AuthSettings(mode, null, null, [], []), repo);

        var check = await v.ValidateAsync(Payload(null), CancellationToken.None);

        Assert.True(check.Ok);
        Assert.Equal("Nombre del token", check.User!.Name);
        Assert.Equal(UserRoles.Cliente, check.Session!.Role);
        Assert.False(check.Session.MustChangePassword);
        Assert.Equal(0, repo.Calls);
    }

    [Fact]
    public async Task Local_UnknownUser_SessionRevoked()
    {
        var f = new LocalAuthFixture();
        var check = await f.Sessions.ValidateAsync(Payload(123), CancellationToken.None);
        Assert.False(check.Ok);
        Assert.Equal("session_revoked", check.FailureCode);
    }

    [Fact]
    public async Task Local_Suspended_AccountSuspended()
    {
        var f = new LocalAuthFixture();
        var acc = await f.CreateUserAsync(status: UserStatuses.Suspendido);
        var check = await f.Sessions.ValidateAsync(Payload(AuthTime.ToUnixMs(acc.PasswordChangedAt)), CancellationToken.None);
        Assert.False(check.Ok);
        Assert.Equal("account_suspended", check.FailureCode);
    }

    [Fact]
    public async Task Local_NullPca_SessionRevoked()
    {
        var f = new LocalAuthFixture();
        await f.CreateUserAsync();
        var check = await f.Sessions.ValidateAsync(Payload(null), CancellationToken.None);
        Assert.Equal("session_revoked", check.FailureCode);
    }

    [Fact]
    public async Task Local_DifferentPca_SessionRevoked()
    {
        var f = new LocalAuthFixture();
        var acc = await f.CreateUserAsync();
        var check = await f.Sessions.ValidateAsync(Payload(AuthTime.ToUnixMs(acc.PasswordChangedAt) - 1), CancellationToken.None);
        Assert.Equal("session_revoked", check.FailureCode);
    }

    [Fact]
    public async Task Local_Ok_FreshDataFromDatabase()
    {
        var f = new LocalAuthFixture();
        var acc = await f.CreateUserAsync(role: UserRoles.Superadmin, mustChange: true);
        acc.Name = "Nombre Editado";
        acc.Sigla = "NE";
        f.Repo.Seed(acc);

        var check = await f.Sessions.ValidateAsync(Payload(AuthTime.ToUnixMs(acc.PasswordChangedAt)), CancellationToken.None);

        Assert.True(check.Ok);
        Assert.Null(check.FailureCode);
        Assert.Equal(Dni, check.User!.Dni);
        Assert.Equal("Nombre Editado", check.User.Name);
        Assert.Equal("NE", check.User.Sigla);
        Assert.Equal(UserRoles.Superadmin, check.Session!.Role);
        Assert.True(check.Session.MustChangePassword);
    }
}
