using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Infrastructure;
using Factum.Backend.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Factum.Backend.Services.Auth;

/// <summary>
/// sesion-larga §5.1: horas en <c>double</c> (D12: <c>0.2</c> = 12 min para probar). Se validan al arrancar
/// con <see cref="JwtSettings.Validate"/>.
/// </summary>
public sealed class JwtOptions
{
    public string Secret { get; set; } = string.Empty;
    /// <summary>Vida de cada token (<c>FACTUM_JWT_EXPIRY_HOURS</c>).</summary>
    public double ExpiryHours { get; set; } = 2;
    /// <summary>Tope absoluto desde el login (<c>FACTUM_SESION_TOPE_HORAS</c>).</summary>
    public double SessionMaxHours { get; set; } = 12;

    public int TokenTtlSeconds => (int)Math.Round(ExpiryHours * 3600);
    public int SessionMaxAgeSeconds => (int)Math.Round(SessionMaxHours * 3600);
}

/// <summary>
/// Lo que trae un JWT válido. <see cref="PasswordChangedAtMs"/> es el claim <c>pca</c> (solo en
/// <c>local</c>); un token viejo sin <c>pca</c> se lee con <c>null</c> (usuarios-locales §6.2).
/// <see cref="AuthTimeS"/> es el claim <c>auth_time</c> (sesion-larga §9.2); null en tokens anteriores.
/// </summary>
public sealed record TokenPayload(User User, long? PasswordChangedAtMs, long? AuthTimeS = null);

/// <summary>
/// sesion-larga §9.2: token emitido y sus duraciones (segundos) para <see cref="LoginResponse"/>.
/// </summary>
public sealed record IssuedToken(string Token, int ExpiresIn, int SessionMaxIn, int TokenTtl, int SessionMaxAge)
{
    /// <summary>Respuesta de login, change-password y refresh (§6.3).</summary>
    public LoginResponse ToResponse(UserDto user) =>
        new(Token, user, ExpiresIn, SessionMaxIn, TokenTtl, SessionMaxAge);
}

/// <summary>sesion-larga §9.2: ventana de un token, pura (segundos Unix).</summary>
public static class SessionWindow
{
    /// <summary>
    /// <c>ExpS = min(now + ttl, auth + maxAge)</c>, <c>MaxS = auth + maxAge</c>. Null si el token ya no
    /// tendría vida (<c>ExpS &lt;= now</c>): se pasó el tope.
    /// </summary>
    public static (long ExpS, long MaxS)? Compute(long nowS, long authS, long ttlS, long maxAgeS)
    {
        var maxS = authS + maxAgeS;
        var expS = Math.Min(nowS + ttlS, maxS);
        return expS <= nowS ? null : (expS, maxS);
    }
}

/// <summary>Truncado a milisegundos y ms Unix de <c>PasswordChangedAt</c> (Mongo guarda ms).</summary>
public static class AuthTime
{
    public static DateTime TruncateToMs(DateTime dt)
    {
        var utc = dt.Kind == DateTimeKind.Local ? dt.ToUniversalTime() : dt;
        return new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    }

    public static long ToUnixMs(DateTime dt) =>
        new DateTimeOffset(DateTime.SpecifyKind(TruncateToMs(dt), DateTimeKind.Utc)).ToUnixTimeMilliseconds();
}

public interface IAuthService
{
    string Mode { get; }
    Task<Result<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default);
    TokenPayload? ValidateToken(string token);
    /// <summary>
    /// Emite un JWT con <c>user</c>, <c>pca</c> (si viene), <c>auth_time</c> e <c>iat</c>. Sin
    /// <paramref name="authTimeS"/>, <c>auth_time</c> es ahora (login, cambio de contraseña).
    /// </summary>
    IssuedToken IssueToken(User user, DateTime? passwordChangedAt, long? authTimeS = null);
    /// <summary>sesion-larga §9.3: vuelve a emitir el token conservando <c>auth_time</c> y <c>pca</c>.</summary>
    Result<LoginResponse> Refresh(TokenPayload payload, User user, AuthSession session);
}

public sealed class AuthService : IAuthService
{
    public const string UserClaim = "user";
    public const string PasswordChangedAtClaim = "pca";
    public const string AuthTimeClaim = "auth_time";
    public const string IssuedAtClaim = "iat";

    private readonly IAuthProvider _provider;
    private readonly JwtOptions _jwt;
    private readonly TimeProvider _time;

    public AuthService(IAuthProvider provider, IOptions<JwtOptions> jwtOpts, TimeProvider? time = null)
    {
        _provider = provider;
        _jwt = jwtOpts.Value;
        _time = time ?? TimeProvider.System;
    }

    public string Mode => _provider.Mode;

    public async Task<Result<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var authResult = await _provider.AuthenticateAsync(request.Dni, request.Username, request.Password, ct);
        if (!authResult.IsSuccess)
            return authResult.Cast<LoginResponse>(); // conserva el code (Details) de local

        var auth = authResult.Value!;
        var issued = IssueToken(auth.User, auth.PasswordChangedAt);
        var dto = new UserDto(auth.User.Dni, auth.User.Name, auth.User.Sigla, auth.Role, auth.MustChangePassword);
        return Result.Ok(issued.ToResponse(dto));
    }

    public Result<LoginResponse> Refresh(TokenPayload payload, User user, AuthSession session)
    {
        // Token anterior a sesion-larga: no se renueva (sigue valiendo hasta su exp).
        if (payload.AuthTimeS is not { } authS)
            return AuthErrors.Fail<LoginResponse>(AuthErrors.SessionMaxAge, AuthErrors.MsgSessionMaxAge);

        var nowS = _time.GetUtcNow().ToUnixTimeSeconds();
        if (SessionWindow.Compute(nowS, authS, _jwt.TokenTtlSeconds, _jwt.SessionMaxAgeSeconds) is null)
            return AuthErrors.Fail<LoginResponse>(AuthErrors.SessionMaxAge, AuthErrors.MsgSessionMaxAge);

        // pca idéntico al del token viejo: ms Unix → DateTime UTC → ToUnixMs devuelve el mismo número.
        DateTime? pca = payload.PasswordChangedAtMs is { } ms
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime
            : null;
        var issued = IssueToken(user, pca, authS);
        var dto = new UserDto(user.Dni, user.Name, user.Sigla, session.Role, session.MustChangePassword);
        return Result.Ok(issued.ToResponse(dto));
    }

    public TokenPayload? ValidateToken(string token)
    {
        try
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Secret));
            var handler = new JwtSecurityTokenHandler();
            handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = false,
                ValidateAudience = false,
                ClockSkew = TimeSpan.Zero,
                // Mismo reloj que la emisión (TimeProvider): en producción es el del sistema.
                LifetimeValidator = (notBefore, expires, _, _) =>
                {
                    var now = _time.GetUtcNow().UtcDateTime;
                    return expires is { } exp && exp > now && (notBefore is not { } nbf || nbf <= now);
                },
            }, out var validated);

            var jwt = (JwtSecurityToken)validated;
            var userJson = jwt.Claims.FirstOrDefault(c => c.Type == UserClaim)?.Value;
            var user = userJson is null ? null : JsonSerializer.Deserialize<User>(userJson);
            if (user is null) return null;

            long? pca = null;
            var rawPca = jwt.Claims.FirstOrDefault(c => c.Type == PasswordChangedAtClaim)?.Value;
            if (rawPca is not null && long.TryParse(rawPca, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
                pca = ms;
            // jwt.Payload: inmune al DefaultInboundClaimTypeMap (que mapea auth_time a una URI).
            var authTime = jwt.Payload.TryGetValue(AuthTimeClaim, out var rawAuth) ? ReadUnixSeconds(rawAuth) : null;
            return new TokenPayload(user, pca, authTime);
        }
        catch
        {
            return null;
        }
    }

    public IssuedToken IssueToken(User user, DateTime? passwordChangedAt, long? authTimeS = null)
    {
        var nowS = _time.GetUtcNow().ToUnixTimeSeconds();
        var auth = authTimeS ?? nowS;
        var ttlS = _jwt.TokenTtlSeconds;
        var maxAgeS = _jwt.SessionMaxAgeSeconds;
        var window = SessionWindow.Compute(nowS, auth, ttlS, maxAgeS)
                     ?? throw new InvalidOperationException("La sesión llegó al tope: no se puede emitir el token.");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        // Solo Dni/Name/Sigla: nunca contraseña, hash, rol ni "debe cambiar" (salen de la base en local).
        var userJson = JsonSerializer.Serialize(new User { Dni = user.Dni, Name = user.Name, Sigla = user.Sigla });

        List<Claim> claims = [new Claim(UserClaim, userJson)];
        if (passwordChangedAt is { } pca)
            claims.Add(new Claim(PasswordChangedAtClaim,
                AuthTime.ToUnixMs(pca).ToString(CultureInfo.InvariantCulture)));
        claims.Add(new Claim(AuthTimeClaim, auth.ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64));
        claims.Add(new Claim(IssuedAtClaim, nowS.ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64));

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTimeOffset.FromUnixTimeSeconds(window.ExpS).UtcDateTime,
            signingCredentials: creds);

        return new IssuedToken(new JwtSecurityTokenHandler().WriteToken(token),
            (int)(window.ExpS - nowS), (int)(window.MaxS - nowS), ttlS, maxAgeS);
    }

    /// <summary>auth_time numérico (long/int/double) o string numérico; si no, null.</summary>
    private static long? ReadUnixSeconds(object? raw) => raw switch
    {
        long l => l,
        int i => i,
        double d when double.IsFinite(d) => (long)d,
        decimal m => (long)m,
        string str when long.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) => v,
        JsonElement { ValueKind: JsonValueKind.Number } e when e.TryGetInt64(out var v) => v,
        JsonElement { ValueKind: JsonValueKind.String } e
            when long.TryParse(e.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) => v,
        _ => null,
    };
}
