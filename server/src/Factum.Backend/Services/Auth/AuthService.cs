using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Factum.Backend.Common;
using Factum.Backend.DTOs;
using Factum.Backend.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Factum.Backend.Services.Auth;

public sealed class JwtOptions
{
    public string Secret { get; set; } = string.Empty;
    public int ExpiryHours { get; set; } = 8;
}

/// <summary>
/// Lo que trae un JWT válido. <see cref="PasswordChangedAtMs"/> es el claim <c>pca</c> (solo en
/// <c>local</c>); un token viejo sin <c>pca</c> se lee con <c>null</c> (usuarios-locales §6.2).
/// </summary>
public sealed record TokenPayload(User User, long? PasswordChangedAtMs);

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
    /// <summary>Emite un JWT con el claim <c>user</c> y, si viene, <c>pca</c>. Lo usa también el cambio de contraseña.</summary>
    string IssueToken(User user, DateTime? passwordChangedAt);
}

public sealed class AuthService : IAuthService
{
    public const string UserClaim = "user";
    public const string PasswordChangedAtClaim = "pca";

    private readonly IAuthProvider _provider;
    private readonly JwtOptions _jwt;

    public AuthService(IAuthProvider provider, IOptions<JwtOptions> jwtOpts)
    {
        _provider = provider;
        _jwt = jwtOpts.Value;
    }

    public string Mode => _provider.Mode;

    public async Task<Result<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var authResult = await _provider.AuthenticateAsync(request.Dni, request.Username, request.Password, ct);
        if (!authResult.IsSuccess)
            return authResult.Cast<LoginResponse>(); // conserva el code (Details) de local

        var auth = authResult.Value!;
        var token = IssueToken(auth.User, auth.PasswordChangedAt);
        var dto = new UserDto(auth.User.Dni, auth.User.Name, auth.User.Sigla, auth.Role, auth.MustChangePassword);
        return Result.Ok(new LoginResponse(token, dto));
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
                ClockSkew = TimeSpan.Zero
            }, out var validated);

            var jwt = (JwtSecurityToken)validated;
            var userJson = jwt.Claims.FirstOrDefault(c => c.Type == UserClaim)?.Value;
            var user = userJson is null ? null : JsonSerializer.Deserialize<User>(userJson);
            if (user is null) return null;

            long? pca = null;
            var rawPca = jwt.Claims.FirstOrDefault(c => c.Type == PasswordChangedAtClaim)?.Value;
            if (rawPca is not null && long.TryParse(rawPca, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
                pca = ms;
            return new TokenPayload(user, pca);
        }
        catch
        {
            return null;
        }
    }

    public string IssueToken(User user, DateTime? passwordChangedAt)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        // Solo Dni/Name/Sigla: nunca contraseña, hash, rol ni "debe cambiar" (salen de la base en local).
        var userJson = JsonSerializer.Serialize(new User { Dni = user.Dni, Name = user.Name, Sigla = user.Sigla });

        List<Claim> claims = [new Claim(UserClaim, userJson)];
        if (passwordChangedAt is { } pca)
            claims.Add(new Claim(PasswordChangedAtClaim,
                AuthTime.ToUnixMs(pca).ToString(CultureInfo.InvariantCulture)));

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddHours(_jwt.ExpiryHours),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
