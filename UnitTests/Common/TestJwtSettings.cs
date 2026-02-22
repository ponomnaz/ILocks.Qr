using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace UnitTests.Common;

internal static class TestJwtSettings
{
    public const string Issuer = "ILocks.Qr.Tests";
    public const string Audience = "ILocks.Qr.Tests.Client";
    public const string Key = "ILocks_Qr_Tests_Jwt_Key_At_Least_32_Chars!";

    public static SymmetricSecurityKey CreateSigningKey()
    {
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key));
    }
}
