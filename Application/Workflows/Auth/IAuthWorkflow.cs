namespace Application.Workflows.Auth;

public interface IAuthWorkflow
{
    Task<RequestOtpWorkflowResult> RequestOtpAsync(string phoneNumber, bool includeDebugCode, CancellationToken ct);
    Task<ConfirmOtpWorkflowResult> ConfirmOtpAsync(string phoneNumber, string code, CancellationToken ct);
}

public enum RequestOtpWorkflowStatus
{
    Success,
    InvalidPhone
}

public sealed record RequestOtpWorkflowData(
    string PhoneNumber,
    DateTimeOffset ExpiresAtUtc,
    int MaxVerifyAttempts,
    string? DebugCode);

public sealed record RequestOtpWorkflowResult(
    RequestOtpWorkflowStatus Status,
    RequestOtpWorkflowData? Data = null);

public enum ConfirmOtpWorkflowStatus
{
    Success,
    InvalidPhone,
    InvalidCodeFormat,
    OtpNotFound,
    OtpExpired,
    OtpBlocked,
    InvalidOtp
}

public sealed record ConfirmOtpWorkflowData(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    Guid UserId,
    string PhoneNumber);

public sealed record ConfirmOtpWorkflowResult(
    ConfirmOtpWorkflowStatus Status,
    ConfirmOtpWorkflowData? Data = null,
    int RemainingAttempts = 0);
