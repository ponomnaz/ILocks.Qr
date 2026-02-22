using Api.Contracts.Auth;
using Api.Contracts.Common;
using Application.Workflows.Auth;

namespace Api.Endpoints;

internal static class AuthEndpoints
{
    private const int OtpCodeLength = 6;

    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var authGroup = app.MapGroup("/api/auth")
            .WithTags("Auth");

        authGroup.MapPost("/request-otp", async (
            RequestOtpRequest request,
            IAuthWorkflow authWorkflow,
            IWebHostEnvironment environment,
            CancellationToken ct) =>
        {
            var result = await authWorkflow.RequestOtpAsync(request.PhoneNumber, environment.IsDevelopment(), ct);

            return result.Status switch
            {
                RequestOtpWorkflowStatus.InvalidPhone => Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["phoneNumber"] = ["Phone number must contain 10-15 digits."]
                }),
                RequestOtpWorkflowStatus.Success when result.Data is not null => Results.Ok(new RequestOtpResponse(
                    result.Data.PhoneNumber,
                    result.Data.ExpiresAtUtc,
                    result.Data.MaxVerifyAttempts,
                    result.Data.DebugCode)),
                _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
            };
        })
            .WithName("RequestOtp")
            .WithSummary("Request OTP by phone")
            .WithDescription("Creates a one-time code for a phone number. In Development the response contains debugCode.")
            .Produces<RequestOtpResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        authGroup.MapPost("/confirm-otp", async (
            ConfirmOtpRequest request,
            IAuthWorkflow authWorkflow,
            CancellationToken ct) =>
        {
            var result = await authWorkflow.ConfirmOtpAsync(request.PhoneNumber, request.Code, ct);

            return result.Status switch
            {
                ConfirmOtpWorkflowStatus.InvalidPhone => Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["phoneNumber"] = ["Phone number must contain 10-15 digits."]
                }),
                ConfirmOtpWorkflowStatus.InvalidCodeFormat => Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["code"] = [$"OTP code must contain exactly {OtpCodeLength} digits."]
                }),
                ConfirmOtpWorkflowStatus.OtpNotFound => Results.BadRequest(new ErrorResponse(
                    "otp_not_found",
                    "OTP code is missing or already consumed. Request a new code.")),
                ConfirmOtpWorkflowStatus.OtpExpired => Results.BadRequest(new ErrorResponse(
                    "otp_expired",
                    "OTP code has expired. Request a new code.")),
                ConfirmOtpWorkflowStatus.OtpBlocked => Results.Problem(
                    title: "Too many OTP attempts",
                    detail: "OTP is blocked. Request a new code.",
                    statusCode: StatusCodes.Status429TooManyRequests),
                ConfirmOtpWorkflowStatus.InvalidOtp => Results.BadRequest(new ConfirmOtpErrorResponse(
                    "invalid_otp",
                    result.RemainingAttempts,
                    result.RemainingAttempts == 0
                        ? "OTP blocked. Request a new code."
                        : "Invalid OTP code.")),
                ConfirmOtpWorkflowStatus.Success when result.Data is not null => Results.Ok(new ConfirmOtpResponse(
                    result.Data.AccessToken,
                    result.Data.ExpiresAtUtc,
                    "Bearer",
                    result.Data.UserId,
                    result.Data.PhoneNumber)),
                _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
            };
        })
            .WithName("ConfirmOtp")
            .WithSummary("Confirm OTP and issue JWT")
            .WithDescription("Verifies OTP for phone number and returns JWT access token.")
            .Produces<ConfirmOtpResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ConfirmOtpErrorResponse>(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        return authGroup;
    }
}
