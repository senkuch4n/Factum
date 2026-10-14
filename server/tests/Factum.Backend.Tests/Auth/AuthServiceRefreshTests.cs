using System.IdentityModel.Tokens.Jwt;
using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Factum.Backend.Services.Auth;
using Microsoft.Extensions.Options;

namespace Factum.Backend.Tests.Auth;

/// <summary>sesion-larga §9.3: <see cref="AuthService.Refresh"/>.</summary>
public sealed class AuthServiceRefreshTests
{
    private static readonly User Ana = new() { Dni = "30111222", Name = "Ana Pérez", Sigla = "AP" };
    private static readonly AuthSession Cliente = new(UserRoles.Cliente, false);

    private static (AuthService Svc, MutableTimeProvider Time) Service(double ttl = 2, double max = 12)
    {
        var time = new MutableTimeProvider();
        var svc = new AuthService(new DevAuthProvider(),
            Options.Create(new JwtOptions { Secret = LocalAuthFixture.Secret, ExpiryHours = ttl, SessionMaxHours = max }),
            time);
        return (svc, time);
    }

    [Fact]
    public void SinAuthTime_SessionMaxAge()
    {
        var (svc, _) = Service();
        var r = svc.Refresh(new TokenPayload(Ana, null, null), Ana, Cliente);
        Assert.False(r.IsSuccess);
        Assert.Equal(AuthErrors.SessionMaxAge, AuthErrors.CodeOf(r));
        Assert.Equal(AuthErrors.MsgSessionMaxAge, r.Error);
    }

    [Fact]
    public void PasadoElTope_SessionMaxAge()
    {
        var (svc, time) = Service();
        var login = svc.ValidateToken(svc.IssueToken(Ana, null).Token)!;
        time.Advance(TimeSpan.FromHours(12).Add(TimeSpan.FromSeconds(1)));
        var r = svc.Refresh(login, Ana, Cliente);
        Assert.Equal(AuthErrors.SessionMaxAge, AuthErrors.CodeOf(r));
    }

    [Fact]
    public void JustoEnElTope_SessionMaxAge()
    {
        var (svc, time) = Service();
        var login = svc.ValidateToken(svc.IssueToken(Ana, null).Token)!;
        time.Advance(TimeSpan.FromHours(12));
        Assert.Equal(AuthErrors.SessionMaxAge, AuthErrors.CodeOf(svc.Refresh(login, Ana, Cliente)));
    }

    [Fact]
    public void Exito_ConservaAuthTimeYPca_ExpRecortado()
    {
        var (svc, time) = Service();
        var pca = new DateTime(2026, 10, 1, 9, 30, 15, 456, DateTimeKind.Utc).AddTicks(789);
        var first = svc.ValidateToken(svc.IssueToken(Ana, pca).Token)!;
        var loginS = time.Now.ToUnixTimeSeconds();
        Assert.Equal(loginS, first.AuthTimeS);

        // Renovaciones sucesivas a lo largo del día.
        var current = first;
        foreach (var hours in new[] { 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5 })
        {
            time.Advance(TimeSpan.FromHours(hours));
            var r = svc.Refresh(current, Ana, Cliente);
            Assert.True(r.IsSuccess, r.Error);
            var next = svc.ValidateToken(r.Value!.Token)!;
            Assert.Equal(loginS, next.AuthTimeS);
            Assert.Equal(first.PasswordChangedAtMs, next.PasswordChangedAtMs);

            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(r.Value.Token);
            var exp = jwt.Payload.Expiration!.Value;
            var nowS = time.Now.ToUnixTimeSeconds();
            Assert.Equal(nowS, Convert.ToInt64(jwt.Payload["iat"]));
            Assert.Equal(Math.Min(nowS + 7200, loginS + 43200), exp);
            Assert.True(exp <= loginS + 43200);
            Assert.Equal(exp - nowS, r.Value.ExpiresIn);
            Assert.Equal(loginS + 43200 - nowS, r.Value.SessionMaxIn);
            Assert.Equal(7200, r.Value.TokenTtl);
            Assert.Equal(43200, r.Value.SessionMaxAge);
            current = next;
        }
        // 10,5 h después del login: el exp quedó recortado al tope (faltan 1,5 h < 2 h).
        Assert.Equal(5400, svc.Refresh(current, Ana, Cliente).Value!.ExpiresIn);
    }

    [Fact]
    public void Exito_UsaElUserFrescoYLaSesion()
    {
        var (svc, _) = Service();
        var payload = svc.ValidateToken(svc.IssueToken(Ana, null).Token)!;
        var fresh = new User { Dni = "30111222", Name = "Ana María Pérez", Sigla = "AMP" };
        var r = svc.Refresh(payload, fresh, new AuthSession(UserRoles.Superadmin, false));
        Assert.True(r.IsSuccess);
        Assert.Equal("Ana María Pérez", r.Value!.User.Name);
        Assert.Equal("AMP", r.Value.User.Sigla);
        Assert.Equal(UserRoles.Superadmin, r.Value.User.Role);
        Assert.Equal("Ana María Pérez", svc.ValidateToken(r.Value.Token)!.User.Name);
    }

    [Fact]
    public void TopeBajadoPorConfig_AplicaAlProximoRefresh()
    {
        var (svc12, time) = Service();
        var payload = svc12.ValidateToken(svc12.IssueToken(Ana, null).Token)!;
        time.Advance(TimeSpan.FromHours(1));
        var svc1 = new AuthService(new DevAuthProvider(),
            Options.Create(new JwtOptions { Secret = LocalAuthFixture.Secret, ExpiryHours = 2, SessionMaxHours = 1 }),
            time);
        Assert.Equal(AuthErrors.SessionMaxAge, AuthErrors.CodeOf(svc1.Refresh(payload, Ana, Cliente)));
    }
}
