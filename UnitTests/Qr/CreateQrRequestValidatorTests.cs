using Api.Contracts.Qr;
using Api.Validation;
using FluentAssertions;

namespace UnitTests.Qr;

public sealed class CreateQrRequestValidatorTests
{
    private readonly CreateQrRequestValidator _validator = new();

    [Fact]
    public void Validate_ValidRequest_ReturnsValidResult()
    {
        var request = BuildValidRequest();

        var result = _validator.Validate(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_CheckInAfterCheckOut_ReturnsErrorForCheckInAt()
    {
        var request = BuildValidRequest() with
        {
            CheckInAt = DateTimeOffset.UtcNow.AddHours(2),
            CheckOutAt = DateTimeOffset.UtcNow.AddHours(1)
        };

        var result = _validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(CreateQrRequest.CheckInAt));
    }

    [Fact]
    public void Validate_CheckOutInPast_ReturnsErrorForCheckOutAt()
    {
        var request = BuildValidRequest() with
        {
            CheckInAt = DateTimeOffset.UtcNow.AddHours(-2),
            CheckOutAt = DateTimeOffset.UtcNow.AddHours(-1)
        };

        var result = _validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(CreateQrRequest.CheckOutAt));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void Validate_GuestsCountOutOfRange_ReturnsErrorForGuestsCount(int guestsCount)
    {
        var request = BuildValidRequest() with { GuestsCount = guestsCount };

        var result = _validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(CreateQrRequest.GuestsCount));
    }

    [Fact]
    public void Validate_EmptyDoorPassword_ReturnsErrorForDoorPassword()
    {
        var request = BuildValidRequest() with { DoorPassword = string.Empty };

        var result = _validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(CreateQrRequest.DoorPassword));
    }

    [Fact]
    public void Validate_EmptyDataType_ReturnsErrorForDataType()
    {
        var request = BuildValidRequest() with { DataType = string.Empty };

        var result = _validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(CreateQrRequest.DataType));
    }

    private static CreateQrRequest BuildValidRequest()
    {
        return new CreateQrRequest(
            CheckInAt: DateTimeOffset.UtcNow.AddHours(1),
            CheckOutAt: DateTimeOffset.UtcNow.AddHours(6),
            GuestsCount: 2,
            DoorPassword: "lock-pass-123",
            DataType: "booking_access");
    }
}
