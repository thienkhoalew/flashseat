using FlashSeat.Events.Domain;
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
        RuleFor(x => x.LayoutX).InclusiveBetween(0, 100).When(x => x.LayoutX.HasValue);
        RuleFor(x => x.LayoutY).InclusiveBetween(0, 100).When(x => x.LayoutY.HasValue);
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
        RuleFor(x => x.EndsAt).GreaterThan(x => x.StartsAt);
        RuleFor(x => x.SalesEndAt).GreaterThan(x => x.SalesStartAt);
        RuleFor(x => x.StartsAt).GreaterThanOrEqualTo(x => x.SalesEndAt);
        RuleFor(x => x.StageShape).Must(BeKnownStageShape).WithMessage("Stage shape is not supported.");
        RuleFor(x => x.StageX).InclusiveBetween(0, 100).When(x => x.StageX.HasValue);
        RuleFor(x => x.StageY).InclusiveBetween(0, 100).When(x => x.StageY.HasValue);
        RuleFor(x => x.StageX).Must((request, stageX) => stageX.HasValue == request.StageY.HasValue).WithMessage("Stage position must include both coordinates.");
        RuleFor(x => x.StageY).Must((request, stageY) => stageY.HasValue == request.StageX.HasValue).WithMessage("Stage position must include both coordinates.");
        RuleFor(x => x.Seats).NotEmpty().Must(HaveUniqueSeatLabels).WithMessage("Seat labels must be unique.");
        RuleForEach(x => x.Seats).SetValidator(new SeatInputValidator());
    }

    private static bool BeKnownStageShape(string value) =>
        Enum.GetNames<StageShape>().Any(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase));

    private static bool BeHttpsUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static bool HaveUniqueSeatLabels(IReadOnlyCollection<SeatInput> seats) =>
        seats.Select(x => $"{x.Section}|{x.Row}|{x.Number}").Distinct(StringComparer.OrdinalIgnoreCase).Count() == seats.Count;
}
