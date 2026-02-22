using System.Security.Cryptography;
using System.Text;
using Application.Security;

namespace Infrastructure.Services;

public sealed class OtpService : IOtpService
{
    public string NormalizePhone(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var digits = value.Where(char.IsDigit).ToArray();
        return new string(digits);
    }

    public bool IsPhoneValid(string normalizedPhone)
    {
        return normalizedPhone.Length is >= 10 and <= 15;
    }

    public string GenerateOtpCode(int length)
    {
        var min = (int)Math.Pow(10, length - 1);
        var max = (int)Math.Pow(10, length);
        return RandomNumberGenerator.GetInt32(min, max).ToString();
    }

    public string ComputeSha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }

    public bool VerifySha256Hex(string value, string expectedHexHash)
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
}
