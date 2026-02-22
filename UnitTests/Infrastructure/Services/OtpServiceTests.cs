using FluentAssertions;
using Infrastructure.Services;

namespace UnitTests.Infrastructure.Services;

public sealed class OtpServiceTests
{
    private readonly OtpService _service = new();

    [Fact]
    public void NormalizePhone_InputWithSymbols_ReturnsDigitsOnly()
    {
        var normalized = _service.NormalizePhone("+7 (999) 123-45-67");

        normalized.Should().Be("79991234567");
    }

    [Theory]
    [InlineData("1234567890", true)]
    [InlineData("123456789012345", true)]
    [InlineData("123456789", false)]
    [InlineData("1234567890123456", false)]
    public void IsPhoneValid_DifferentLengths_ReturnsExpectedResult(string normalizedPhone, bool expected)
    {
        var result = _service.IsPhoneValid(normalizedPhone);

        result.Should().Be(expected);
    }

    [Fact]
    public void GenerateOtpCode_ValidLength_ReturnsDigitsWithRequestedLength()
    {
        const int length = 6;

        var code = _service.GenerateOtpCode(length);

        code.Should().HaveLength(length);
        code.All(char.IsDigit).Should().BeTrue();
    }

    [Fact]
    public void VerifySha256Hex_CorrectCodeAndHash_ReturnsTrue()
    {
        const string code = "123456";
        var hash = _service.ComputeSha256Hex(code);

        var result = _service.VerifySha256Hex(code, hash);

        result.Should().BeTrue();
    }

    [Fact]
    public void VerifySha256Hex_WrongCode_ReturnsFalse()
    {
        const string code = "123456";
        var hash = _service.ComputeSha256Hex(code);

        var result = _service.VerifySha256Hex("654321", hash);

        result.Should().BeFalse();
    }

    [Fact]
    public void VerifySha256Hex_InvalidHashFormat_ReturnsFalse()
    {
        var result = _service.VerifySha256Hex("123456", "not-a-hex-value");

        result.Should().BeFalse();
    }
}
