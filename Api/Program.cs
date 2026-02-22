using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Domain.Entities;
using FluentValidation;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

const int OtpCodeLength = 6;
const int OtpExpiresInMinutes = 5;
const int OtpMaxVerifyAttempts = 5;
const int AccessTokenExpiresInHours = 12;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
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

app.UseAuthentication();
app.UseAuthorization();

var authGroup = app.MapGroup("/api/auth");

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
});

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
});

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "ILocks.Qr.Api",
    environment = app.Environment.EnvironmentName
})).WithName("Health").WithOpenApi();

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
public sealed record ErrorResponse(string ErrorCode, string Message);
public sealed record ConfirmOtpErrorResponse(string ErrorCode, int RemainingAttempts, string Message);
file sealed record AccessTokenData(string AccessToken, DateTimeOffset ExpiresAtUtc);
