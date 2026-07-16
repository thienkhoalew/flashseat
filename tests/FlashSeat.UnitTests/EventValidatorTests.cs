using FlashSeat.Events.Application;
using FluentAssertions;
using Xunit;

namespace FlashSeat.UnitTests;

public sealed class EventValidatorTests
{
    [Fact]
    public void Event_rejects_duplicate_seats_and_insecure_image_url()
    {
        var now = DateTimeOffset.UtcNow;
        var request = new SaveEventRequest("Valid Event", "valid-event", "", "http://example.com/image.jpg",
            "Venue", "Address", now.AddDays(2), now, now.AddDays(1),
            [new("VIP", "A", 1, 100_000), new("VIP", "A", 1, 100_000)]);

        var result = new SaveEventRequestValidator().Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Select(x => x.PropertyName).Should().Contain(["ImageUrl", "Seats"]);
    }
}
