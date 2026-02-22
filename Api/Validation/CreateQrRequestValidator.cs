using Api.Contracts.Qr;
using FluentValidation;

public sealed class CreateQrRequestValidator : AbstractValidator<CreateQrRequest>
{
    public CreateQrRequestValidator()
    {
        RuleFor(x => x.CheckInAt)
            .LessThan(x => x.CheckOutAt)
            .WithMessage("CheckInAt must be earlier than CheckOutAt.");

        RuleFor(x => x.CheckOutAt)
            .Must(value => value > DateTimeOffset.UtcNow)
            .WithMessage("CheckOutAt must be in the future.");

        RuleFor(x => x.GuestsCount)
            .InclusiveBetween(1, 50);

        RuleFor(x => x.DoorPassword)
            .NotEmpty()
            .MaximumLength(128);

        RuleFor(x => x.DataType)
            .NotEmpty()
            .MaximumLength(64);
    }
}
