using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Infrastructure.Services;
using UnitTests.Common;

namespace UnitTests.Auth;

public sealed class JwtTokenServiceTests
{
    [Fact]
    public void CreateAccessToken_ValidInput_ReturnsTokenWithExpectedClaims()
    {
        var lifetime = TimeSpan.FromHours(12);
        var userId = Guid.NewGuid();
        const string phoneNumber = "79991234567";
        var beforeCall = DateTimeOffset.UtcNow;
        var service = new JwtTokenService(
            TestJwtSettings.Issuer,
            TestJwtSettings.Audience,
            TestJwtSettings.CreateSigningKey(),
            lifetime);

        var tokenData = service.CreateAccessToken(userId, phoneNumber);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tokenData.AccessToken);

        jwt.Issuer.Should().Be(TestJwtSettings.Issuer);
        jwt.Audiences.Should().Contain(TestJwtSettings.Audience);
        jwt.Claims.First(x => x.Type == JwtRegisteredClaimNames.Sub).Value.Should().Be(userId.ToString());
        jwt.Claims.First(x => x.Type == ClaimTypes.NameIdentifier).Value.Should().Be(userId.ToString());
        jwt.Claims.First(x => x.Type == ClaimTypes.MobilePhone).Value.Should().Be(phoneNumber);
        jwt.Claims.First(x => x.Type == JwtRegisteredClaimNames.Jti).Value.Should().NotBeNullOrWhiteSpace();

        tokenData.ExpiresAtUtc.Should().BeOnOrAfter(beforeCall.Add(lifetime).AddSeconds(-5));
        tokenData.ExpiresAtUtc.Should().BeOnOrBefore(beforeCall.Add(lifetime).AddSeconds(5));
    }

    [Fact]
    public void CreateAccessToken_TwoCalls_ReturnDifferentJti()
    {
        var service = new JwtTokenService(
            TestJwtSettings.Issuer,
            TestJwtSettings.Audience,
            TestJwtSettings.CreateSigningKey(),
            TimeSpan.FromHours(12));
        var userId = Guid.NewGuid();

        var firstToken = new JwtSecurityTokenHandler().ReadJwtToken(service.CreateAccessToken(userId, "79991234567").AccessToken);
        var secondToken = new JwtSecurityTokenHandler().ReadJwtToken(service.CreateAccessToken(userId, "79991234567").AccessToken);

        var firstJti = firstToken.Claims.First(x => x.Type == JwtRegisteredClaimNames.Jti).Value;
        var secondJti = secondToken.Claims.First(x => x.Type == JwtRegisteredClaimNames.Jti).Value;

        firstJti.Should().NotBe(secondJti);
    }
}
