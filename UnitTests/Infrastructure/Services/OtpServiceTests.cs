using FluentAssertions;
using Infrastructure.Services;

namespace UnitTests.Infrastructure.Services;

public sealed class OtpServiceTests
{
    private readonly OtpService _sut = new();

    [Fact]
    public void NormalizePhone_RemovesNonDigits()
    {
        var result = _sut.NormalizePhone("+7 (999) 123-45-67 ext.89");

        result.Should().Be("7999123456789");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizePhone_EmptyInput_ReturnsEmpty(string? input)
    {
        var result = _sut.NormalizePhone(input!);

        result.Should().BeEmpty();
    }

    [Fact]
    public void IsPhoneValid_LengthLessThan10_ReturnsFalse()
    {
        var result = _sut.IsPhoneValid("123456789");

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("1234567890")]
    [InlineData("123456789012345")]
    public void IsPhoneValid_Length10To15_ReturnsTrue(string phone)
    {
        var result = _sut.IsPhoneValid(phone);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsPhoneValid_LengthGreaterThan15_ReturnsFalse()
    {
        var result = _sut.IsPhoneValid("1234567890123456");

        result.Should().BeFalse();
    }

    [Fact]
    public void GenerateOtpCode_ReturnsDigitsWithRequestedLength()
    {
        var result = _sut.GenerateOtpCode(6);

        result.Should().HaveLength(6);
        result.All(char.IsDigit).Should().BeTrue();
    }

    [Fact]
    public void VerifySha256Hex_CorrectValue_ReturnsTrue()
    {
        var hash = _sut.ComputeSha256Hex("123456");

        var result = _sut.VerifySha256Hex("123456", hash);

        result.Should().BeTrue();
    }

    [Fact]
    public void VerifySha256Hex_WrongValue_ReturnsFalse()
    {
        var hash = _sut.ComputeSha256Hex("123456");

        var result = _sut.VerifySha256Hex("000000", hash);

        result.Should().BeFalse();
    }

    [Fact]
    public void VerifySha256Hex_InvalidHashFormat_ReturnsFalse()
    {
        var result = _sut.VerifySha256Hex("123456", "not-a-hex-value");

        result.Should().BeFalse();
    }
}
