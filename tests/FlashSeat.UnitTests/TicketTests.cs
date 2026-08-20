using FlashSeat.Booking.Domain;
using FlashSeat.Booking.Infrastructure;
using FluentAssertions;
using Xunit;

namespace FlashSeat.UnitTests;

public sealed class TicketTests
{
    [Fact]
    public void Ticket_code_is_raw_uppercase_128_bit_hex()
    {
        var code = TicketCodeGenerator.Create();

        code.Should().MatchRegex("^[0-9A-F]{32}$");
        TicketCodeGenerator.TryParse($"FS1:{code}", out var parsed).Should().BeTrue();
        parsed.Should().Be(code);
    }

    [Theory]
    [InlineData("FS1-0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("FS1:0123456789ABCDEF0123456789ABCDE")]
    [InlineData("fs1:0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("FS1:0123456789ABCDEF0123456789ABCDEG")]
    public void Ticket_parser_rejects_invalid_envelopes(string value)
    {
        TicketCodeGenerator.TryParse(value, out _).Should().BeFalse();
    }

    [Fact]
    public void Check_in_records_operator_and_time_once()
    {
        var item = new BookingItem(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Main", "A", 1, 100, "VND", TicketCodeGenerator.Create());
        var operatorId = Guid.NewGuid();
        var checkedInAt = DateTimeOffset.UtcNow;

        item.CheckIn(operatorId, checkedInAt);

        item.CheckInStatus.Should().Be(TicketCheckInStatus.CheckedIn);
        item.CheckedInBy.Should().Be(operatorId);
        item.CheckedInAt.Should().Be(checkedInAt);
        var action = () => item.CheckIn(Guid.NewGuid(), checkedInAt.AddMinutes(1));
        action.Should().Throw<InvalidOperationException>();
    }
}
