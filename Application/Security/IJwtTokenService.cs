namespace Application.Security;

public interface IJwtTokenService
{
    AccessTokenData CreateAccessToken(Guid userId, string phoneNumber);
}

public sealed record AccessTokenData(string AccessToken, DateTimeOffset ExpiresAtUtc);
