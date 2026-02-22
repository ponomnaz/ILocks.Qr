using System.Text;
using Api.Endpoints;
using FluentValidation;
using Infrastructure.Persistence;
using Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ILocks.Qr API",
        Version = "v1",
        Description = "Backend API for OTP login, QR generation/history, and Telegram delivery."
    });

    var bearerScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "JWT Bearer token. Example: Bearer {token}",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Reference = new OpenApiReference
        {
            Type = ReferenceType.SecurityScheme,
            Id = "Bearer"
        }
    };

    options.AddSecurityDefinition("Bearer", bearerScheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            bearerScheme,
            Array.Empty<string>()
        }
    });
});
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
}

var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];
var jwtKey = builder.Configuration["Jwt:Key"];
var telegramBotToken = builder.Configuration["Telegram:BotToken"];

if (string.IsNullOrWhiteSpace(jwtIssuer) || string.IsNullOrWhiteSpace(jwtAudience) || string.IsNullOrWhiteSpace(jwtKey))
{
    throw new InvalidOperationException("Jwt settings are not configured.");
}

if (jwtKey.Length < 32)
{
    throw new InvalidOperationException("Jwt:Key must contain at least 32 characters.");
}

if (telegramBotToken is null)
{
    throw new InvalidOperationException("Telegram:BotToken key is not configured.");
}

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});
builder.Services.AddSingleton<IQrCodeService, QrCodeService>();
builder.Services.AddSingleton<ITelegramQrSender, TelegramQrSender>();

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints(jwtIssuer, jwtAudience, signingKey);
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
