using FluentValidation;
namespace FlashSeat.Payment.Application;
public sealed class CreatePaymentRequestValidator : AbstractValidator<CreatePaymentRequest>
{
    public CreatePaymentRequestValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.SimulateResult).Must(x => x is "Success" or "Failed");
    }
}
