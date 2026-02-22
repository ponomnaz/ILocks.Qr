using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Api.Endpoints;

internal static class AuthEndpoints
{
    private const int OtpCodeLength = 6;
    private const int OtpExpiresInMinutes = 5;
    private const int OtpMaxVerifyAttempts = 5;
    private const int AccessTokenExpiresInHours = 12;

    public static RouteGroupBuilder MapAuthEndpoints(
        this IEndpointRouteBuilder app,
        string jwtIssuer,
        string jwtAudience,
        SymmetricSecurityKey signingKey)
    {
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

        return authGroup;
    }

    private static string NormalizePhone(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var digits = value.Where(char.IsDigit).ToArray();
        return new string(digits);
    }

    private static bool IsPhoneValid(string phone)
    {
        return phone.Length is >= 10 and <= 15;
    }

    private static string GenerateOtpCode(int length)
    {
        var min = (int)Math.Pow(10, length - 1);
        var max = (int)Math.Pow(10, length);
        return RandomNumberGenerator.GetInt32(min, max).ToString();
    }

    private static string ComputeSha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }

    private static bool VerifySha256Hex(string value, string expectedHexHash)
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

    private static AccessTokenData CreateAccessToken(
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

    private sealed record AccessTokenData(string AccessToken, DateTimeOffset ExpiresAtUtc);
}
