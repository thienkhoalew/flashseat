using FlashSeat.Events.Domain;
using FluentAssertions;
using Xunit;

namespace FlashSeat.UnitTests;

public sealed class EventLifecycleTests
{
    [Theory]
    [InlineData(EventStatus.Draft)]
    [InlineData(EventStatus.Published)]
    [InlineData(EventStatus.Cancelled)]
    public void Event_is_ended_at_or_after_end_time(EventStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        var entity = CreateEvent(now.AddHours(-2), now.AddHours(-1));
        if (status != EventStatus.Draft)
        {
            typeof(EventEntity).GetProperty(nameof(EventEntity.Status))!
                .SetValue(entity, status);
        }

        entity.EffectiveStatus(now).Should().Be(EventStatus.Ended);
    }

    [Fact]
    public void Published_event_can_return_to_draft_before_sales_start()
    {
        var now = DateTimeOffset.UtcNow;
        var entity = CreateEvent(now.AddDays(1), now.AddDays(1).AddHours(2));
        entity.Publish(now);
        entity.Unpublish(now.AddMinutes(1));

        entity.Status.Should().Be(EventStatus.Draft);
    }

    [Fact]
    public void Cancelled_event_can_be_republished_before_it_ends()
    {
        var now = DateTimeOffset.UtcNow;
        var entity = CreateEvent(now.AddDays(1), now.AddDays(1).AddHours(2));
        entity.Cancel(now);
        entity.Republish(now.AddMinutes(1));

        entity.Status.Should().Be(EventStatus.Published);
    }

    [Fact]
    public void Ended_event_can_be_archived()
    {
        var now = DateTimeOffset.UtcNow;
        var entity = CreateEvent(now.AddHours(-2), now.AddHours(-1));

        entity.Archive(now);

        entity.DeletedAt.Should().Be(now);
    }

    [Fact]
    public void Cancelled_event_can_be_archived_after_sales_activity()
    {
        var now = DateTimeOffset.UtcNow;
        var entity = CreateEvent(now.AddDays(1), now.AddDays(1).AddHours(2));
        entity.Cancel(now);

        entity.Archive(now.AddMinutes(1));

        entity.DeletedAt.Should().Be(now.AddMinutes(1));
    }

    [Fact]
    public void Ended_event_rejects_lifecycle_changes_except_archiving()
    {
        var now = DateTimeOffset.UtcNow;
        var entity = CreateEvent(now.AddHours(-2), now.AddHours(-1));

        var action = () => entity.Cancel(now);

        action.Should().Throw<InvalidOperationException>();
    }

    private static EventEntity CreateEvent(DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        var now = DateTimeOffset.UtcNow;
        var entity = new EventEntity(Guid.NewGuid(), "Lifecycle event", "lifecycle-event", "Description",
            "https://example.com/event.jpg", "Venue", "Address", startsAt, endsAt,
            now.AddHours(1), startsAt.AddHours(-1), now);
        entity.Seats.Add(new Seat(Guid.NewGuid(), entity.Id, "VIP", "A", 1, 100, "VND"));
        return entity;
    }
}
