using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Api.Endpoints;
using FluentAssertions;

namespace UnitTests.Api.Endpoints;

public sealed class EndpointUserContextTests
{
    [Fact]
    public void TryGetUserId_NameIdentifierClaim_ReturnsTrueAndGuid()
    {
        var expected = Guid.NewGuid();
        var principal = BuildPrincipal(new Claim(ClaimTypes.NameIdentifier, expected.ToString()));

        var result = EndpointUserContext.TryGetUserId(principal, out var actual);

        result.Should().BeTrue();
        actual.Should().Be(expected);
    }

    [Fact]
    public void TryGetUserId_SubClaimFallback_ReturnsTrueAndGuid()
    {
        var expected = Guid.NewGuid();
        var principal = BuildPrincipal(new Claim(JwtRegisteredClaimNames.Sub, expected.ToString()));

        var result = EndpointUserContext.TryGetUserId(principal, out var actual);

        result.Should().BeTrue();
        actual.Should().Be(expected);
    }

    [Fact]
    public void TryGetUserId_InvalidGuid_ReturnsFalse()
    {
        var principal = BuildPrincipal(new Claim(ClaimTypes.NameIdentifier, "not-a-guid"));

        var result = EndpointUserContext.TryGetUserId(principal, out var actual);

        result.Should().BeFalse();
        actual.Should().Be(Guid.Empty);
    }

    [Fact]
    public void TryGetUserId_NoClaims_ReturnsFalse()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var result = EndpointUserContext.TryGetUserId(principal, out var actual);

        result.Should().BeFalse();
        actual.Should().Be(Guid.Empty);
    }

    private static ClaimsPrincipal BuildPrincipal(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
