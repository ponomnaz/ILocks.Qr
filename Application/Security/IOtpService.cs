namespace Application.Security;

public interface IOtpService
{
    string NormalizePhone(string value);
    bool IsPhoneValid(string normalizedPhone);
    string GenerateOtpCode(int length);
    string ComputeSha256Hex(string value);
    bool VerifySha256Hex(string value, string expectedHexHash);
}
