namespace Api.Contracts.Auth;

public sealed record ConfirmOtpResponse(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    string TokenType,
    Guid UserId,
    string PhoneNumber);
