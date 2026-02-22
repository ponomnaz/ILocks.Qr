using Api.Configuration;
using Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

var runtimeSettings = builder.Configuration.GetApiRuntimeSettings();
var signingKey = runtimeSettings.CreateJwtSigningKey();

builder.Services
    .AddApiFoundation()
    .AddApiInfrastructure(runtimeSettings)
    .AddApiAuthentication(runtimeSettings, signingKey);

var app = builder.Build();

app.UseApiPipeline();

app.MapAuthEndpoints(runtimeSettings.JwtIssuer, runtimeSettings.JwtAudience, signingKey);
app.MapTelegramEndpoints();
app.MapQrEndpoints();
app.MapHealthEndpoints();

app.Run();

public sealed record RequestOtpRequest(string PhoneNumber);
public sealed record RequestOtpResponse(string PhoneNumber, DateTimeOffset ExpiresAtUtc, int MaxVerifyAttempts, string? DebugCode);
public sealed record ConfirmOtpRequest(string PhoneNumber, string Code);
public sealed record ConfirmOtpResponse(string AccessToken, DateTimeOffset ExpiresAtUtc, string TokenType, Guid UserId, string PhoneNumber);
public sealed record CreateQrRequest(DateTimeOffset CheckInAt, DateTimeOffset CheckOutAt, int GuestsCount, string DoorPassword, string DataType);
public sealed record CreateQrResponse(
    Guid Id,
    DateTimeOffset CheckInAt,
    DateTimeOffset CheckOutAt,
    int GuestsCount,
    string DataType,
    DateTimeOffset CreatedAt,
    string PayloadJson,
    string QrImageBase64);
public sealed record QrCodeListItemResponse(
    Guid Id,
    DateTimeOffset CheckInAt,
    DateTimeOffset CheckOutAt,
    int GuestsCount,
    string DataType,
    DateTimeOffset CreatedAt);
public sealed record QrCodeHistoryResponse(
    IReadOnlyList<QrCodeListItemResponse> Items,
    int Total,
    int Skip,
    int Take);
public sealed record QrCodeDetailsResponse(
    Guid Id,
    DateTimeOffset CheckInAt,
    DateTimeOffset CheckOutAt,
    int GuestsCount,
    string DoorPassword,
    string DataType,
    DateTimeOffset CreatedAt,
    string PayloadJson,
    string QrImageBase64);
public sealed record BindTelegramChatRequest(long ChatId);
public sealed record BindTelegramChatResponse(Guid UserId, long ChatId, DateTimeOffset BoundAtUtc);
public sealed record SendQrToTelegramResponse(Guid QrId, long ChatId, DateTimeOffset SentAtUtc, string Status);
public sealed record ErrorResponse(string ErrorCode, string Message);
public sealed record ConfirmOtpErrorResponse(string ErrorCode, int RemainingAttempts, string Message);
