namespace Api.Contracts.Auth;

public sealed record ConfirmOtpErrorResponse(string ErrorCode, int RemainingAttempts, string Message);
