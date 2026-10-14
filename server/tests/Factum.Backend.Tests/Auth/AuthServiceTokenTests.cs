using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Factum.Backend.Services.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Factum.Backend.Tests.Auth;

/// <summary>usuarios-locales §6.2 (B31).</summary>
public sealed class AuthServiceTokenTests
{
    private const string Secret = LocalAuthFixture.Secret;
    private static readonly User Ana = new() { Dni = "30111222", Name = "Ana Pérez", Sigla = "AP" };

    private static AuthService Service(double expiryHours = 8, string secret = Secret, double maxHours = 12,
        TimeProvider? time = null) =>
        new(new DevAuthProvider(),
            Options.Create(new JwtOptions { Secret = secret, ExpiryHours = expiryHours, SessionMaxHours = maxHours }),
            time);

    [Fact]
    public void IssueAndValidate_RoundTripWithPca()
    {
        var changedAt = new DateTime(2026, 10, 6, 12, 0, 0, 123, DateTimeKind.Utc).AddTicks(4567);
        var svc = Service();
        var payload = svc.ValidateToken(svc.IssueToken(Ana, changedAt).Token);

        Assert.NotNull(payload);
        Assert.Equal("30111222", payload.User.Dni);
        Assert.Equal("Ana Pérez", payload.User.Name);
        Assert.Equal("AP", payload.User.Sigla);
        // Truncado a ms: igual al valor que devolvería Mongo.
        Assert.Equal(AuthTime.ToUnixMs(AuthTime.TruncateToMs(changedAt)), payload.PasswordChangedAtMs);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 12, 0, 0, 123, TimeSpan.Zero).ToUnixTimeMilliseconds(),
            payload.PasswordChangedAtMs);
    }

    [Fact]
    public void Issue_WithoutPca_DevStyle()
    {
        var svc = Service();
        var payload = svc.ValidateToken(svc.IssueToken(Ana, null).Token);
        Assert.NotNull(payload);
        Assert.Null(payload.PasswordChangedAtMs);
    }

    [Fact]
    public void AlteredSignature_Null()
    {
        var svc = Service();
        var token = svc.IssueToken(Ana, DateTime.UtcNow).Token;
        // Se cambia el primer carácter de la firma (el último solo lleva bits de relleno).
        var sigStart = token.LastIndexOf('.') + 1;
        var swapped = token[sigStart] == 'A' ? 'B' : 'A';
        Assert.Null(svc.ValidateToken(token[..sigStart] + swapped + token[(sigStart + 1)..]));
        Assert.Null(Service(secret: "otro-secreto-de-al-menos-32-caracteres-yy").ValidateToken(token));
        Assert.Null(svc.ValidateToken("no-es-un-jwt"));
    }

    [Fact]
    public void Expired_Null()
    {
        // sesion-larga: emitido hace 3 h con vida de 2 h (el reloj del servicio se adelanta para validar).
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow.AddHours(-3));
        var svc = Service(expiryHours: 2, time: time);
        var token = svc.IssueToken(Ana, DateTime.UtcNow).Token;
        Assert.NotNull(svc.ValidateToken(token));
        time.Advance(TimeSpan.FromHours(2));
        Assert.Null(svc.ValidateToken(token));
        Assert.Null(Service().ValidateToken(token)); // reloj del sistema
    }

    [Fact]
    public void Payload_HasNoPasswordNorHashNorRole()
    {
        var hash = new Pbkdf2PasswordHasher(1000).Hash("la-contraseña-123");
        var svc = Service();
        var token = svc.IssueToken(Ana, DateTime.UtcNow).Token;
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var raw = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(token.Split('.')[1]));

        Assert.DoesNotContain("PasswordHash", raw);
        Assert.DoesNotContain("la-contraseña-123", raw);
        Assert.DoesNotContain(hash, raw);
        Assert.DoesNotContain("superadmin", raw);
        Assert.DoesNotContain("MustChangePassword", raw);
        Assert.Equal(["user", "pca", "auth_time", "iat", "exp"], jwt.Claims.Select(c => c.Type));
    }

    [Fact]
    public void OldTokenWithoutPca_IsReadWithNull()
    {
        // Formato de antes de usuarios-locales: solo el claim "user".
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
        var old = new JwtSecurityToken(
            claims: [new Claim("user", JsonSerializer.Serialize(Ana))],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        var token = new JwtSecurityTokenHandler().WriteToken(old);

        var payload = Service().ValidateToken(token);
        Assert.NotNull(payload);
        Assert.Equal("30111222", payload.User.Dni);
        Assert.Null(payload.PasswordChangedAtMs);
    }

    [Fact]
    public async Task DevLogin_UserDtoHasClienteAndNoMustChange()
    {
        var r = await Service().LoginAsync(new LoginRequest("30111222", "x", "ana.perez"));
        Assert.True(r.IsSuccess);
        Assert.Equal(new UserDto("30111222", "Ana Perez", "-", "cliente", false), r.Value!.User);
        Assert.Null(Service().ValidateToken(r.Value.Token)!.PasswordChangedAtMs);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task DevLogin_WithoutUsername_UsuarioRequerido(string? username)
    {
        var r = await Service().LoginAsync(new LoginRequest("30111222", "x", username));
        Assert.False(r.IsSuccess);
        Assert.Equal("Usuario requerido", r.Error);
        Assert.Null(AuthErrors.CodeOf(r)); // dev: sin code, como siempre
    }

    [Fact]
    public async Task LocalLogin_PropagatesCodeAndFlags()
    {
        var f = new LocalAuthFixture();
        await f.CreateUserAsync(role: UserRoles.Superadmin);

        var bad = await f.Auth.LoginAsync(new LoginRequest("30111222", "incorrecta"));
        Assert.Equal("invalid_credentials", AuthErrors.CodeOf(bad));

        var ok = await f.Auth.LoginAsync(new LoginRequest("30111222", "temporal-123"));
        Assert.True(ok.IsSuccess);
        Assert.Equal("superadmin", ok.Value!.User.Role);
        Assert.True(ok.Value.User.MustChangePassword);
        Assert.Equal(AuthTime.ToUnixMs(f.Repo.Get("30111222")!.PasswordChangedAt),
            f.Auth.ValidateToken(ok.Value.Token)!.PasswordChangedAtMs);
    }

    [Fact]
    public void UserDto_SerializesSnakeCase()
    {
        var json = JsonSerializer.Serialize(
            new LoginResponse("t", new UserDto("1", "n", "s", "cliente", true), 7200, 43200, 7200, 43200),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        Assert.Equal("""{"token":"t","user":{"dni":"1","name":"n","sigla":"s","role":"cliente","must_change_password":true},"expires_in":7200,"session_max_in":43200,"token_ttl":7200,"session_max_age":43200}""", json);
        var req = JsonSerializer.Deserialize<ChangePasswordRequest>(
            """{"current_password":"a","new_password":"b","new_password_confirmation":"c"}""",
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        Assert.Equal(new ChangePasswordRequest("a", "b", "c"), req);
    }

    // ── sesion-larga §11.1: auth_time, iat y duraciones ─────────────────────

    private static string RawPayload(string token) =>
        Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(token.Split('.')[1]));

    [Fact]
    public void Payload_AuthTimeEIat_SonNumericos()
    {
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var nowS = time.Now.ToUnixTimeSeconds();
        var raw = RawPayload(Service(time: time).IssueToken(Ana, null).Token);
        Assert.Contains($"\"auth_time\":{nowS}", raw);
        Assert.Contains($"\"iat\":{nowS}", raw);
    }

    [Fact]
    public async Task Login_AuthTimeEsAhora_YExpMenosIatEsTtl()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var r = await Service(expiryHours: 2).LoginAsync(new LoginRequest("30111222", "x", "ana.perez"));
        var after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.True(r.IsSuccess);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(r.Value!.Token);
        var auth = Convert.ToInt64(jwt.Payload["auth_time"]);
        var iat = Convert.ToInt64(jwt.Payload["iat"]);
        Assert.InRange(auth, before, after);
        Assert.Equal(auth, iat);
        Assert.Equal(7200, jwt.Payload.Expiration!.Value - iat);
        Assert.Equal(7200, r.Value.ExpiresIn);
        Assert.Equal(43200, r.Value.SessionMaxIn);
        Assert.Equal(7200, r.Value.TokenTtl);
        Assert.Equal(43200, r.Value.SessionMaxAge);
    }

    [Fact]
    public void HorasFraccionarias_0_2_Son720Segundos()
    {
        var issued = Service(expiryHours: 0.2, maxHours: 0.5).IssueToken(Ana, null);
        Assert.Equal(720, issued.ExpiresIn);
        Assert.Equal(720, issued.TokenTtl);
        Assert.Equal(1800, issued.SessionMaxAge);
        Assert.Equal(1800, issued.SessionMaxIn);
    }

    [Fact]
    public void ValidateToken_LeeAuthTime()
    {
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var svc = Service(time: time);
        var authS = time.Now.ToUnixTimeSeconds() - 600;
        var payload = svc.ValidateToken(svc.IssueToken(Ana, null, authS).Token);
        Assert.NotNull(payload);
        Assert.Equal(authS, payload.AuthTimeS);
    }

    [Fact]
    public void ValidateToken_SinAuthTime_Null()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
        var old = new JwtSecurityToken(
            claims: [new Claim("user", JsonSerializer.Serialize(Ana))],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        var payload = Service().ValidateToken(new JwtSecurityTokenHandler().WriteToken(old));
        Assert.NotNull(payload);
        Assert.Null(payload.AuthTimeS);
    }
}
