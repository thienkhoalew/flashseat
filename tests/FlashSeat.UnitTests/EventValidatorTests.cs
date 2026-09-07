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
            "SalesEndAt",
            "StartsAt",
            "Seats[0].Number",
            "Seats[0].Price",
            "Seats[0].Currency"
        ]);
    }

    [Fact]
    public void Event_accepts_known_stage_shapes_and_seat_coordinates()
    {
        var result = new SaveEventRequestValidator().Validate(ValidRequest() with
        {
            StageShape = "intheround",
            Seats = [new("VIP", "A", 1, 100_000, "VND", 0, 100)]
        });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Event_rejects_unknown_stage_shapes_and_out_of_range_coordinates()
    {
        var result = new SaveEventRequestValidator().Validate(ValidRequest() with
        {
            StageShape = "99",
            Seats = [new("VIP", "A", 1, 100_000, "VND", -1, 101)]
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Select(x => x.PropertyName).Should().Contain([
            "StageShape",
            "Seats[0].LayoutX",
            "Seats[0].LayoutY"
        ]);
    }

    [Fact]
    public void Event_accepts_a_complete_stage_position()
    {
        var result = new SaveEventRequestValidator().Validate(ValidRequest() with { StageX = 25.5m, StageY = 72.25m });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Event_rejects_a_partial_stage_position()
    {
        var result = new SaveEventRequestValidator().Validate(ValidRequest() with { StageX = 25.5m });

        result.IsValid.Should().BeFalse();
        result.Errors.Select(x => x.PropertyName).Should().Contain(nameof(SaveEventRequest.StageX));
    }

    [Fact]
    public void Event_rejects_out_of_range_stage_position()
    {
        var result = new SaveEventRequestValidator().Validate(ValidRequest() with { StageX = -0.01m, StageY = 100.01m });

        result.IsValid.Should().BeFalse();
        result.Errors.Select(x => x.PropertyName).Should().Contain([nameof(SaveEventRequest.StageX), nameof(SaveEventRequest.StageY)]);
    }

    [Fact]
    public void Event_accepts_legacy_payload_without_layout_fields()
    {
        var result = new SaveEventRequestValidator().Validate(ValidRequest());

        result.IsValid.Should().BeTrue();
    }

    private static SaveEventRequest ValidRequest()
    {
        var now = DateTimeOffset.UtcNow;
        return new SaveEventRequest("Valid Event", "valid-event", "Description", "https://example.com/image.jpg",
            "Venue", "Address", now.AddDays(2), now.AddDays(2).AddHours(3), now, now.AddDays(1), [new("VIP", "A", 1, 100_000)]);
    }
}
