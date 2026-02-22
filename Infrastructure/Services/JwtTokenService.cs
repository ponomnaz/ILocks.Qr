using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Application.Security;
using Microsoft.IdentityModel.Tokens;

namespace Infrastructure.Services;

public sealed class JwtTokenService(
    string issuer,
    string audience,
    SymmetricSecurityKey signingKey,
    TimeSpan accessTokenLifetime) : IJwtTokenService
{
    public AccessTokenData CreateAccessToken(Guid userId, string phoneNumber)
    {
        var now = DateTime.UtcNow;
        var expiresAtUtc = now.Add(accessTokenLifetime);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.MobilePhone, phoneNumber),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: now,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return new AccessTokenData(
            new JwtSecurityTokenHandler().WriteToken(token),
            new DateTimeOffset(expiresAtUtc, TimeSpan.Zero));
    }
}
