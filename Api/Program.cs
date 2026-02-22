using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Domain.Entities;
using FluentValidation;
using Infrastructure.Persistence;
using Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

const int OtpCodeLength = 6;
const int OtpExpiresInMinutes = 5;
const int OtpMaxVerifyAttempts = 5;
const int AccessTokenExpiresInHours = 12;

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

var authGroup = app.MapGroup("/api/auth")
    .WithTags("Auth");

authGroup.MapPost("/request-otp", async (
    RequestOtpRequest request,
    AppDbContext db,
    IWebHostEnvironment environment,
    CancellationToken ct) =>
{
    var normalizedPhone = NormalizePhone(request.PhoneNumber);
    if (!IsPhoneValid(normalizedPhone))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["phoneNumber"] = ["Phone number must contain 10-15 digits."]
        });
    }

    var now = DateTimeOffset.UtcNow;

    // A single active OTP per phone. New request invalidates previous pending codes.
    var activeCodes = await db.OtpCodes
        .Where(x => x.PhoneNumber == normalizedPhone && !x.IsUsed)
        .ToListAsync(ct);

    foreach (var activeCode in activeCodes)
    {
        activeCode.IsUsed = true;
    }

    var otpCode = GenerateOtpCode(OtpCodeLength);

    var otp = new OtpCode
    {
        Id = Guid.NewGuid(),
        PhoneNumber = normalizedPhone,
        CodeHash = ComputeSha256Hex(otpCode),
        ExpiresAt = now.AddMinutes(OtpExpiresInMinutes),
        FailedAttempts = 0,
        IsUsed = false,
        CreatedAt = now
    };

    db.OtpCodes.Add(otp);
    await db.SaveChangesAsync(ct);

    var response = new RequestOtpResponse(
        normalizedPhone,
        otp.ExpiresAt,
        OtpMaxVerifyAttempts,
        environment.IsDevelopment() ? otpCode : null);

    return Results.Ok(response);
})
    .WithName("RequestOtp")
    .WithSummary("Request OTP by phone")
    .WithDescription("Creates a one-time code for a phone number. In Development the response contains debugCode.")
    .Produces<RequestOtpResponse>(StatusCodes.Status200OK)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest);

authGroup.MapPost("/confirm-otp", async (
    ConfirmOtpRequest request,
    AppDbContext db,
    CancellationToken ct) =>
{
    var normalizedPhone = NormalizePhone(request.PhoneNumber);
    if (!IsPhoneValid(normalizedPhone))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["phoneNumber"] = ["Phone number must contain 10-15 digits."]
        });
    }

    if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length != OtpCodeLength || !request.Code.All(char.IsDigit))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["code"] = [$"OTP code must contain exactly {OtpCodeLength} digits."]
        });
    }

    var now = DateTimeOffset.UtcNow;

    var otp = await db.OtpCodes
        .Where(x => x.PhoneNumber == normalizedPhone && !x.IsUsed)
        .OrderByDescending(x => x.CreatedAt)
        .FirstOrDefaultAsync(ct);

    if (otp is null)
    {
        return Results.BadRequest(new ErrorResponse(
            "otp_not_found",
            "OTP code is missing or already consumed. Request a new code."));
    }

    if (otp.ExpiresAt <= now)
    {
        otp.IsUsed = true;
        await db.SaveChangesAsync(ct);

        return Results.BadRequest(new ErrorResponse(
            "otp_expired",
            "OTP code has expired. Request a new code."));
    }

    if (otp.FailedAttempts >= OtpMaxVerifyAttempts)
    {
        otp.IsUsed = true;
        await db.SaveChangesAsync(ct);

        return Results.Problem(
            title: "Too many OTP attempts",
            detail: "OTP is blocked. Request a new code.",
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    if (!VerifySha256Hex(request.Code, otp.CodeHash))
    {
        otp.FailedAttempts += 1;

        if (otp.FailedAttempts >= OtpMaxVerifyAttempts)
        {
            otp.IsUsed = true;
            await db.SaveChangesAsync(ct);

            return Results.Problem(
                title: "Too many OTP attempts",
                detail: "OTP is blocked. Request a new code.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        await db.SaveChangesAsync(ct);

        var remainingAttempts = Math.Max(0, OtpMaxVerifyAttempts - otp.FailedAttempts);
        return Results.BadRequest(new ConfirmOtpErrorResponse(
            "invalid_otp",
            remainingAttempts,
            remainingAttempts == 0
                ? "OTP blocked. Request a new code."
                : "Invalid OTP code."));
    }

    otp.IsUsed = true;

    var user = await db.Users.SingleOrDefaultAsync(x => x.PhoneNumber == normalizedPhone, ct);
    if (user is null)
    {
        user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = normalizedPhone,
            CreatedAt = now
        };

        db.Users.Add(user);
    }

    var tokenData = CreateAccessToken(
        user.Id,
        normalizedPhone,
        jwtIssuer,
        jwtAudience,
        signingKey,
        TimeSpan.FromHours(AccessTokenExpiresInHours));

    await db.SaveChangesAsync(ct);

    return Results.Ok(new ConfirmOtpResponse(
        tokenData.AccessToken,
        tokenData.ExpiresAtUtc,
        "Bearer",
        user.Id,
        normalizedPhone));
})
    .WithName("ConfirmOtp")
    .WithSummary("Confirm OTP and issue JWT")
    .WithDescription("Verifies OTP for phone number and returns JWT access token.")
    .Produces<ConfirmOtpResponse>(StatusCodes.Status200OK)
    .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
    .Produces<ConfirmOtpErrorResponse>(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status429TooManyRequests);

var telegramGroup = app.MapGroup("/api/telegram")
    .WithTags("Telegram")
    .RequireAuthorization();

telegramGroup.MapPost("/bind-chat", async (
    BindTelegramChatRequest request,
    ClaimsPrincipal principal,
    AppDbContext db,
    CancellationToken ct) =>
{
    if (request.ChatId <= 0)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["chatId"] = ["ChatId must be greater than zero."]
        });
    }

    if (!TryGetUserId(principal, out var userId))
    {
        return Results.Unauthorized();
    }

    var userExists = await db.Users.AnyAsync(x => x.Id == userId, ct);
    if (!userExists)
    {
        return Results.Unauthorized();
    }

    var existingByChat = await db.TelegramBindings
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.ChatId == request.ChatId, ct);

    if (existingByChat is not null && existingByChat.UserId != userId)
    {
        return Results.Conflict(new ErrorResponse(
            "telegram_chat_already_bound",
            "This Telegram chat is already linked to another user."));
    }

    var now = DateTimeOffset.UtcNow;

    var binding = await db.TelegramBindings
        .FirstOrDefaultAsync(x => x.UserId == userId, ct);

    if (binding is null)
    {
        binding = new TelegramBinding
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ChatId = request.ChatId,
            CreatedAt = now
        };

        db.TelegramBindings.Add(binding);
    }
    else
    {
        binding.ChatId = request.ChatId;
        binding.CreatedAt = now;
    }

    await db.SaveChangesAsync(ct);

    return Results.Ok(new BindTelegramChatResponse(
        binding.UserId,
        binding.ChatId,
        binding.CreatedAt));
})
    .WithName("BindTelegramChat")
    .WithSummary("Bind Telegram chat")
    .WithDescription("Binds Telegram chatId to current user. One chat cannot be shared between users.")
    .Produces<BindTelegramChatResponse>(StatusCodes.Status200OK)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
    .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
    .Produces(StatusCodes.Status401Unauthorized);

var qrGroup = app.MapGroup("/api/qr")
    .WithTags("QR")
    .RequireAuthorization();

qrGroup.MapPost("", async (
    CreateQrRequest request,
    ClaimsPrincipal principal,
    AppDbContext db,
    IQrCodeService qrCodeService,
    IValidator<CreateQrRequest> validator,
    CancellationToken ct) =>
{
    var validationResult = await validator.ValidateAsync(request, ct);
    if (!validationResult.IsValid)
    {
        return Results.ValidationProblem(validationResult.ToDictionary());
    }

    if (!TryGetUserId(principal, out var userId))
    {
        return Results.Unauthorized();
    }

    var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct);
    if (user is null)
    {
        return Results.Unauthorized();
    }

    var now = DateTimeOffset.UtcNow;
    var dataType = string.IsNullOrWhiteSpace(request.DataType)
        ? "booking_access"
        : request.DataType.Trim();

    var payloadObject = new
    {
        checkInAt = request.CheckInAt,
        checkOutAt = request.CheckOutAt,
        guestsCount = request.GuestsCount,
        doorPassword = request.DoorPassword,
        userId = user.Id,
        phoneNumber = user.PhoneNumber,
        dataType,
        createdAt = now
    };

    var payloadJson = JsonSerializer.Serialize(payloadObject);
    var qrImageBase64 = qrCodeService.GeneratePngBase64(payloadJson);

    var record = new QrCodeRecord
    {
        Id = Guid.NewGuid(),
        UserId = user.Id,
        CheckInAt = request.CheckInAt,
        CheckOutAt = request.CheckOutAt,
        GuestsCount = request.GuestsCount,
        DoorPassword = request.DoorPassword.Trim(),
        PayloadJson = payloadJson,
        QrImageBase64 = qrImageBase64,
        DataType = dataType,
        CreatedAt = now
    };

    db.QrCodeRecords.Add(record);
    await db.SaveChangesAsync(ct);

    return Results.Ok(new CreateQrResponse(
        record.Id,
        record.CheckInAt,
        record.CheckOutAt,
        record.GuestsCount,
        record.DataType,
        record.CreatedAt,
        record.PayloadJson,
        record.QrImageBase64));
})
    .WithName("CreateQr")
    .WithSummary("Generate and save QR")
    .WithDescription("Generates PNG QR from booking payload and stores record in database for current user.")
    .Produces<CreateQrResponse>(StatusCodes.Status200OK)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized);

qrGroup.MapGet("", async (
    ClaimsPrincipal principal,
    AppDbContext db,
    int? skip,
    int? take,
    CancellationToken ct) =>
{
    if (!TryGetUserId(principal, out var userId))
    {
        return Results.Unauthorized();
    }

    var resolvedSkip = Math.Max(0, skip ?? 0);
    var resolvedTake = Math.Clamp(take ?? 20, 1, 100);

    var userRecordsQuery = db.QrCodeRecords
        .AsNoTracking()
        .Where(x => x.UserId == userId);

    var total = await userRecordsQuery.CountAsync(ct);

    var items = await userRecordsQuery
        .OrderByDescending(x => x.CreatedAt)
        .Skip(resolvedSkip)
        .Take(resolvedTake)
        .Select(x => new QrCodeListItemResponse(
            x.Id,
            x.CheckInAt,
            x.CheckOutAt,
            x.GuestsCount,
            x.DataType,
            x.CreatedAt))
        .ToListAsync(ct);

    return Results.Ok(new QrCodeHistoryResponse(items, total, resolvedSkip, resolvedTake));
})
    .WithName("GetQrHistory")
    .WithSummary("Get QR history")
    .WithDescription("Returns paged QR records for current user.")
    .Produces<QrCodeHistoryResponse>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized);

qrGroup.MapGet("/{id:guid}", async (
    Guid id,
    ClaimsPrincipal principal,
    AppDbContext db,
    CancellationToken ct) =>
{
    if (!TryGetUserId(principal, out var userId))
    {
        return Results.Unauthorized();
    }

    var record = await db.QrCodeRecords
        .AsNoTracking()
        .Where(x => x.Id == id && x.UserId == userId)
        .Select(x => new QrCodeDetailsResponse(
            x.Id,
            x.CheckInAt,
            x.CheckOutAt,
            x.GuestsCount,
            x.DoorPassword,
            x.DataType,
            x.CreatedAt,
            x.PayloadJson,
            x.QrImageBase64))
        .SingleOrDefaultAsync(ct);

    if (record is null)
    {
        return Results.NotFound(new ErrorResponse(
            "qr_not_found",
            "QR record not found."));
    }

    return Results.Ok(record);
})
    .WithName("GetQrById")
    .WithSummary("Get QR details by id")
    .WithDescription("Returns single QR record for current user.")
    .Produces<QrCodeDetailsResponse>(StatusCodes.Status200OK)
    .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status401Unauthorized);

qrGroup.MapPost("/{id:guid}/send-telegram", async (
    Guid id,
    ClaimsPrincipal principal,
    AppDbContext db,
    ITelegramQrSender telegramQrSender,
    CancellationToken ct) =>
{
    if (!TryGetUserId(principal, out var userId))
    {
        return Results.Unauthorized();
    }

    var record = await db.QrCodeRecords
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);

    if (record is null)
    {
        return Results.NotFound(new ErrorResponse(
            "qr_not_found",
            "QR record not found."));
    }

    var binding = await db.TelegramBindings
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.UserId == userId, ct);

    if (binding is null)
    {
        return Results.BadRequest(new ErrorResponse(
            "telegram_not_bound",
            "Telegram chat is not bound for this user."));
    }

    var caption = $"QR access ({record.DataType}) | {record.CheckInAt:O} - {record.CheckOutAt:O}";

    try
    {
        await telegramQrSender.SendQrCodeAsync(binding.ChatId, record.QrImageBase64, caption, ct);
    }
    catch (TelegramIntegrationException ex) when (ex.Error == TelegramIntegrationError.Configuration)
    {
        return Results.Problem(
            title: "Telegram is not configured",
            detail: ex.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (TelegramIntegrationException ex) when (ex.Error == TelegramIntegrationError.InvalidChat)
    {
        return Results.BadRequest(new ErrorResponse("telegram_invalid_chat", ex.Message));
    }
    catch (TelegramIntegrationException ex) when (ex.Error == TelegramIntegrationError.Forbidden)
    {
        return Results.Problem(
            title: "Telegram access denied",
            detail: ex.Message,
            statusCode: StatusCodes.Status403Forbidden);
    }
    catch (TelegramIntegrationException ex) when (ex.Error == TelegramIntegrationError.Timeout)
    {
        return Results.Problem(
            title: "Telegram timeout",
            detail: ex.Message,
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
    catch (TelegramIntegrationException ex) when (ex.Error == TelegramIntegrationError.Network)
    {
        return Results.Problem(
            title: "Telegram network failure",
            detail: ex.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (TelegramIntegrationException ex) when (ex.Error == TelegramIntegrationError.InvalidPayload)
    {
        return Results.Problem(
            title: "Stored QR payload is invalid",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
    catch (TelegramIntegrationException ex)
    {
        return Results.Problem(
            title: "Telegram API error",
            detail: ex.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }

    return Results.Ok(new SendQrToTelegramResponse(
        record.Id,
        binding.ChatId,
        DateTimeOffset.UtcNow,
        "sent"));
})
    .WithName("SendQrToTelegram")
    .WithSummary("Send QR to Telegram")
    .WithDescription("Sends stored QR PNG to bound Telegram chat for current user.")
    .Produces<SendQrToTelegramResponse>(StatusCodes.Status200OK)
    .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
    .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status401Unauthorized)
    .ProducesProblem(StatusCodes.Status403Forbidden)
    .ProducesProblem(StatusCodes.Status500InternalServerError)
    .ProducesProblem(StatusCodes.Status502BadGateway)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
    .ProducesProblem(StatusCodes.Status504GatewayTimeout);

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "ILocks.Qr.Api",
    environment = app.Environment.EnvironmentName
}))
    .WithName("Health")
    .WithSummary("Health check")
    .WithDescription("Basic API health endpoint.")
    .Produces(StatusCodes.Status200OK)
    .WithOpenApi();

app.Run();

static string NormalizePhone(string value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return string.Empty;
    }

    var digits = value.Where(char.IsDigit).ToArray();
    return new string(digits);
}

static bool IsPhoneValid(string phone)
{
    return phone.Length is >= 10 and <= 15;
}

static string GenerateOtpCode(int length)
{
    var min = (int)Math.Pow(10, length - 1);
    var max = (int)Math.Pow(10, length);
    return RandomNumberGenerator.GetInt32(min, max).ToString();
}

static string ComputeSha256Hex(string value)
{
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
    return Convert.ToHexString(bytes);
}

static bool VerifySha256Hex(string value, string expectedHexHash)
{
    var currentHash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
    byte[] expectedHash;

    try
    {
        expectedHash = Convert.FromHexString(expectedHexHash);
    }
    catch (FormatException)
    {
        return false;
    }

    return CryptographicOperations.FixedTimeEquals(currentHash, expectedHash);
}

static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
{
    var userIdRaw = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);

    return Guid.TryParse(userIdRaw, out userId);
}

static AccessTokenData CreateAccessToken(
    Guid userId,
    string phoneNumber,
    string issuer,
    string audience,
    SymmetricSecurityKey signingKey,
    TimeSpan lifetime)
{
    var now = DateTime.UtcNow;
    var expiresAtUtc = now.Add(lifetime);

    var claims = new List<Claim>
    {
        new(JwtRegisteredClaimNames.Sub, userId.ToString()),
        new(ClaimTypes.NameIdentifier, userId.ToString()),
        new(ClaimTypes.MobilePhone, phoneNumber),
        new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
    };

    var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
    var token = new JwtSecurityToken(
        issuer: issuer,
        audience: audience,
        claims: claims,
        notBefore: now,
        expires: expiresAtUtc,
        signingCredentials: credentials);

    return new AccessTokenData(
        new JwtSecurityTokenHandler().WriteToken(token),
        new DateTimeOffset(expiresAtUtc, TimeSpan.Zero));
}

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
file sealed record AccessTokenData(string AccessToken, DateTimeOffset ExpiresAtUtc);
