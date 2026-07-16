using FluentValidation;

namespace FlashSeat.Booking.Application;

public sealed class CreateHoldRequestValidator : AbstractValidator<CreateHoldRequest>
{
    public CreateHoldRequestValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.SeatIds).NotEmpty().Must(x => x.Count <= 6).WithMessage("A hold can contain at most 6 seats.")
            .Must(x => x.Distinct().Count() == x.Count).WithMessage("Seat IDs must be unique.");
    }
}

public sealed class CreateBookingRequestValidator : AbstractValidator<CreateBookingRequest>
{
    public CreateBookingRequestValidator() => RuleFor(x => x.HoldId).NotEmpty();
}
