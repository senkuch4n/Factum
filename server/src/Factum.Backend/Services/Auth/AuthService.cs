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

public interface IAuthService
{
    string Mode { get; }
    Task<Result<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default);
    User? ValidateToken(string token);
}

public sealed class AuthService : IAuthService
{
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
            return Result.Fail<LoginResponse>(authResult.Error!);

        var user = authResult.Value!;
        var token = GenerateToken(user);
        var dto = new UserDto(user.Dni, user.Name, user.Sigla);
        return Result.Ok(new LoginResponse(token, dto));
    }

    public User? ValidateToken(string token)
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
            var userJson = jwt.Claims.FirstOrDefault(c => c.Type == "user")?.Value;
            return userJson is null ? null : JsonSerializer.Deserialize<User>(userJson);
        }
        catch
        {
            return null;
        }
    }

    private string GenerateToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var userJson = JsonSerializer.Serialize(user);

        var token = new JwtSecurityToken(
            claims: [new Claim("user", userJson)],
            expires: DateTime.UtcNow.AddHours(_jwt.ExpiryHours),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
