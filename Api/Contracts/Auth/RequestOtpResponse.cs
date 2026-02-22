namespace Api.Contracts.Auth;

public sealed record RequestOtpResponse(
    string PhoneNumber,
    DateTimeOffset ExpiresAtUtc,
    int MaxVerifyAttempts,
    string? DebugCode);
