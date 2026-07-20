using FlashSeat.Events.Application;
using FluentAssertions;
using Xunit;

namespace FlashSeat.UnitTests;

public sealed class EventValidatorTests
{
    [Fact]
    public void Event_accepts_a_valid_admin_payload()
    {
        new SaveEventRequestValidator().Validate(ValidRequest()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Event_rejects_duplicate_seats_and_insecure_image_url()
    {
        var request = ValidRequest() with
        {
            ImageUrl = "http://example.com/image.jpg",
            Seats = [new("VIP", "A", 1, 100_000), new("VIP", "A", 1, 100_000)]
        };

        var result = new SaveEventRequestValidator().Validate(request);

        result.Errors.Select(x => x.PropertyName).Should().Contain(["ImageUrl", "Seats"]);
    }

    [Fact]
    public void Event_rejects_invalid_schedule_and_seat_values()
    {
        var now = DateTimeOffset.UtcNow;
        var request = ValidRequest() with
        {
            StartsAt = now.AddHours(1),
            SalesStartAt = now.AddHours(3),
            SalesEndAt = now.AddHours(2),
            Seats = [new("VIP", "A", 0, 0, "vn")]
        };

        var result = new SaveEventRequestValidator().Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Select(x => x.PropertyName).Should().Contain([
            "SalesEndAt", "StartsAt", "Seats[0].Number", "Seats[0].Price", "Seats[0].Currency"
        ]);
    }

    private static SaveEventRequest ValidRequest()
    {
        var now = DateTimeOffset.UtcNow;
        return new SaveEventRequest("Valid Event", "valid-event", "Description", "https://example.com/image.jpg",
            "Venue", "Address", now.AddDays(2), now, now.AddDays(1), [new("VIP", "A", 1, 100_000)]);
    }
}
