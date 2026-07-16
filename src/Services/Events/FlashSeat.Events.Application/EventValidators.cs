using FluentValidation;

namespace FlashSeat.Events.Application;

public sealed class SeatInputValidator : AbstractValidator<SeatInput>
{
    public SeatInputValidator()
    {
        RuleFor(x => x.Section).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Row).NotEmpty().MaximumLength(10);
        RuleFor(x => x.Number).GreaterThan(0);
        RuleFor(x => x.Price).GreaterThan(0);
        RuleFor(x => x.Currency).Length(3).Matches("^[A-Z]{3}$");
    }
}

public sealed class SaveEventRequestValidator : AbstractValidator<SaveEventRequest>
{
    public SaveEventRequestValidator()
    {
        RuleFor(x => x.Name).Length(3, 150);
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(160).Matches("^[a-z0-9]+(?:-[a-z0-9]+)*$");
        RuleFor(x => x.Description).NotNull().MaximumLength(5000);
        RuleFor(x => x.ImageUrl).NotEmpty().MaximumLength(2048).Must(BeHttpsUrl);
        RuleFor(x => x.VenueName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Address).NotEmpty().MaximumLength(500);
        RuleFor(x => x.SalesEndAt).GreaterThan(x => x.SalesStartAt);
        RuleFor(x => x.StartsAt).GreaterThanOrEqualTo(x => x.SalesEndAt);
        RuleFor(x => x.Seats).NotEmpty().Must(HaveUniqueSeatLabels).WithMessage("Seat labels must be unique.");
        RuleForEach(x => x.Seats).SetValidator(new SeatInputValidator());
    }

    private static bool BeHttpsUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static bool HaveUniqueSeatLabels(IReadOnlyCollection<SeatInput> seats) =>
        seats.Select(x => $"{x.Section}|{x.Row}|{x.Number}").Distinct(StringComparer.OrdinalIgnoreCase).Count() == seats.Count;
}
