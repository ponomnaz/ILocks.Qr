using FluentAssertions;
using Infrastructure.Services;

namespace UnitTests.Infrastructure.Services;

public sealed class QrCodeServiceTests
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    private readonly QrCodeService _sut = new();

    [Fact]
    public void GeneratePngBase64_ValidPayload_ReturnsNonEmptyBase64()
    {
        var result = _sut.GeneratePngBase64("{\"key\":\"value\"}");

        result.Should().NotBeNullOrWhiteSpace();
        var action = () => Convert.FromBase64String(result);
        action.Should().NotThrow();
    }

    [Fact]
    public void GeneratePngBase64_ResultHasPngSignature()
    {
        var result = _sut.GeneratePngBase64("{\"key\":\"value\"}");
        var bytes = Convert.FromBase64String(result);

        bytes.Should().HaveCountGreaterThanOrEqualTo(PngSignature.Length);
        bytes[..PngSignature.Length].Should().Equal(PngSignature);
    }
}
