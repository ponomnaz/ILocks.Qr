using Api.Contracts.Qr;
using Api.Validation;
using FluentValidation.TestHelper;

namespace UnitTests.Api.Validation;

public sealed class CreateQrRequestValidatorTests
{
    private readonly CreateQrRequestValidator _sut = new();

    [Fact]
    public void Validate_ValidModel_Passes()
    {
        var model = CreateValidModel();

        var result = _sut.TestValidate(model);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_CheckInAfterOrEqualCheckOut_Fails()
    {
        var now = DateTimeOffset.UtcNow.AddHours(2);
        var model = new CreateQrRequest(
            now,
            now,
            2,
            "1234",
            "booking_access");

        var result = _sut.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.CheckInAt);
    }

    [Fact]
    public void Validate_CheckOutNotInFuture_Fails()
    {
        var model = new CreateQrRequest(
            DateTimeOffset.UtcNow.AddHours(-2),
            DateTimeOffset.UtcNow.AddMinutes(-1),
            2,
            "1234",
            "booking_access");

        var result = _sut.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.CheckOutAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void Validate_GuestsCountOutOfRange_Fails(int guestsCount)
    {
        var validModel = CreateValidModel();
        var model = validModel with { GuestsCount = guestsCount };

        var result = _sut.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.GuestsCount);
    }

    [Fact]
    public void Validate_DoorPasswordEmpty_Fails()
    {
        var validModel = CreateValidModel();
        var model = validModel with { DoorPassword = string.Empty };

        var result = _sut.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.DoorPassword);
    }

    [Fact]
    public void Validate_DoorPasswordTooLong_Fails()
    {
        var validModel = CreateValidModel();
        var model = validModel with { DoorPassword = new string('a', 129) };

        var result = _sut.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.DoorPassword);
    }

    [Fact]
    public void Validate_DataTypeEmpty_Fails()
    {
        var validModel = CreateValidModel();
        var model = validModel with { DataType = string.Empty };

        var result = _sut.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.DataType);
    }

    [Fact]
    public void Validate_DataTypeTooLong_Fails()
    {
        var validModel = CreateValidModel();
        var model = validModel with { DataType = new string('a', 65) };

        var result = _sut.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.DataType);
    }

    private static CreateQrRequest CreateValidModel()
    {
        return new CreateQrRequest(
            DateTimeOffset.UtcNow.AddHours(1),
            DateTimeOffset.UtcNow.AddHours(2),
            2,
            "1234",
            "booking_access");
    }
}
