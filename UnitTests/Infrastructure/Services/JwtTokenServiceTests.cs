using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Infrastructure.Services;
using Microsoft.IdentityModel.Tokens;

namespace UnitTests.Infrastructure.Services;

public sealed class JwtTokenServiceTests
{
    [Fact]
    public void CreateAccessToken_ReturnsNonEmptyToken()
    {
        var sut = CreateSut(TimeSpan.FromMinutes(30));

        var result = sut.CreateAccessToken(Guid.NewGuid(), "79990001122");

        result.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void CreateAccessToken_ContainsRequiredClaims()
    {
        var userId = Guid.NewGuid();
        const string phone = "79990001122";
        var sut = CreateSut(TimeSpan.FromMinutes(30));

        var result = sut.CreateAccessToken(userId, phone);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken);

        jwt.Claims.Should().Contain(x => x.Type == JwtRegisteredClaimNames.Sub && x.Value == userId.ToString());
        jwt.Claims.Should().Contain(x => x.Type == ClaimTypes.NameIdentifier && x.Value == userId.ToString());
        jwt.Claims.Should().Contain(x => x.Type == ClaimTypes.MobilePhone && x.Value == phone);
        var jtiClaim = jwt.Claims.Single(x => x.Type == JwtRegisteredClaimNames.Jti);
        Guid.TryParse(jtiClaim.Value, out var parsedJti).Should().BeTrue();
        parsedJti.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void CreateAccessToken_HasExpectedIssuerAndAudience()
    {
        var sut = CreateSut(TimeSpan.FromMinutes(30));

        var result = sut.CreateAccessToken(Guid.NewGuid(), "79990001122");
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken);

        jwt.Issuer.Should().Be("test-issuer");
        jwt.Audiences.Should().ContainSingle().Which.Should().Be("test-audience");
    }

    [Fact]
    public void CreateAccessToken_HasExpectedExpirationRange()
    {
        var lifetime = TimeSpan.FromMinutes(30);
        var sut = CreateSut(lifetime);
        var before = DateTimeOffset.UtcNow;

        var result = sut.CreateAccessToken(Guid.NewGuid(), "79990001122");

        var after = DateTimeOffset.UtcNow;
        result.ExpiresAtUtc.Should().BeOnOrAfter(before.Add(lifetime).AddSeconds(-1));
        result.ExpiresAtUtc.Should().BeOnOrBefore(after.Add(lifetime).AddSeconds(1));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken);
        var jwtExpiresAt = new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero);
        jwtExpiresAt.Should().BeCloseTo(result.ExpiresAtUtc, TimeSpan.FromSeconds(1));
    }

    private static JwtTokenService CreateSut(TimeSpan lifetime)
    {
        var keyBytes = Encoding.UTF8.GetBytes("12345678901234567890123456789012");
        var key = new SymmetricSecurityKey(keyBytes);

        return new JwtTokenService("test-issuer", "test-audience", key, lifetime);
    }
}
