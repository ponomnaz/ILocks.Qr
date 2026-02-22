using Application.Security;
using Application.Workflows.Auth;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Workflows;

public sealed class AuthWorkflow(
    AppDbContext db,
    IOtpService otpService,
    IJwtTokenService jwtTokenService) : IAuthWorkflow
{
    private const int OtpCodeLength = 6;
    private const int OtpExpiresInMinutes = 5;
    private const int OtpMaxVerifyAttempts = 5;

    public async Task<RequestOtpWorkflowResult> RequestOtpAsync(string phoneNumber, bool includeDebugCode, CancellationToken ct)
    {
        var normalizedPhone = otpService.NormalizePhone(phoneNumber);
        if (!otpService.IsPhoneValid(normalizedPhone))
        {
            return new RequestOtpWorkflowResult(RequestOtpWorkflowStatus.InvalidPhone);
        }

        var now = DateTimeOffset.UtcNow;

        var activeCodes = await db.OtpCodes
            .Where(x => x.PhoneNumber == normalizedPhone && !x.IsUsed)
            .ToListAsync(ct);

        foreach (var activeCode in activeCodes)
        {
            activeCode.IsUsed = true;
        }

        var otpCode = otpService.GenerateOtpCode(OtpCodeLength);

        var otp = new OtpCode
        {
            Id = Guid.NewGuid(),
            PhoneNumber = normalizedPhone,
            CodeHash = otpService.ComputeSha256Hex(otpCode),
            ExpiresAt = now.AddMinutes(OtpExpiresInMinutes),
            FailedAttempts = 0,
            IsUsed = false,
            CreatedAt = now
        };

        db.OtpCodes.Add(otp);
        await db.SaveChangesAsync(ct);

        return new RequestOtpWorkflowResult(
            RequestOtpWorkflowStatus.Success,
            new RequestOtpWorkflowData(
                normalizedPhone,
                otp.ExpiresAt,
                OtpMaxVerifyAttempts,
                includeDebugCode ? otpCode : null));
    }

    public async Task<ConfirmOtpWorkflowResult> ConfirmOtpAsync(string phoneNumber, string code, CancellationToken ct)
    {
        var normalizedPhone = otpService.NormalizePhone(phoneNumber);
        if (!otpService.IsPhoneValid(normalizedPhone))
        {
            return new ConfirmOtpWorkflowResult(ConfirmOtpWorkflowStatus.InvalidPhone);
        }

        if (string.IsNullOrWhiteSpace(code) || code.Length != OtpCodeLength || !code.All(char.IsDigit))
        {
            return new ConfirmOtpWorkflowResult(ConfirmOtpWorkflowStatus.InvalidCodeFormat);
        }

        var now = DateTimeOffset.UtcNow;

        var otp = await db.OtpCodes
            .Where(x => x.PhoneNumber == normalizedPhone && !x.IsUsed)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (otp is null)
        {
            return new ConfirmOtpWorkflowResult(ConfirmOtpWorkflowStatus.OtpNotFound);
        }

        if (otp.ExpiresAt <= now)
        {
            otp.IsUsed = true;
            await db.SaveChangesAsync(ct);

            return new ConfirmOtpWorkflowResult(ConfirmOtpWorkflowStatus.OtpExpired);
        }

        if (otp.FailedAttempts >= OtpMaxVerifyAttempts)
        {
            otp.IsUsed = true;
            await db.SaveChangesAsync(ct);

            return new ConfirmOtpWorkflowResult(ConfirmOtpWorkflowStatus.OtpBlocked);
        }

        if (!otpService.VerifySha256Hex(code, otp.CodeHash))
        {
            otp.FailedAttempts += 1;

            if (otp.FailedAttempts >= OtpMaxVerifyAttempts)
            {
                otp.IsUsed = true;
                await db.SaveChangesAsync(ct);

                return new ConfirmOtpWorkflowResult(ConfirmOtpWorkflowStatus.OtpBlocked);
            }

            await db.SaveChangesAsync(ct);

            var remainingAttempts = Math.Max(0, OtpMaxVerifyAttempts - otp.FailedAttempts);
            return new ConfirmOtpWorkflowResult(ConfirmOtpWorkflowStatus.InvalidOtp, RemainingAttempts: remainingAttempts);
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

        var tokenData = jwtTokenService.CreateAccessToken(user.Id, normalizedPhone);

        await db.SaveChangesAsync(ct);

        return new ConfirmOtpWorkflowResult(
            ConfirmOtpWorkflowStatus.Success,
            new ConfirmOtpWorkflowData(
                tokenData.AccessToken,
                tokenData.ExpiresAtUtc,
                user.Id,
                normalizedPhone));
    }
}
