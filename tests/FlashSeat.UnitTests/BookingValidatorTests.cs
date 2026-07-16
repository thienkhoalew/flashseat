using FlashSeat.Booking.Application;
using FluentAssertions;
using Xunit;
namespace FlashSeat.UnitTests;
public sealed class BookingValidatorTests
{
    [Fact] public void Hold_rejects_more_than_six_seats()
    { var result=new CreateHoldRequestValidator().Validate(new CreateHoldRequest(Guid.NewGuid(),Enumerable.Range(0,7).Select(_=>Guid.NewGuid()).ToArray())); result.IsValid.Should().BeFalse(); }
    [Fact] public void Hold_rejects_duplicate_seats()
    { var id=Guid.NewGuid(); var result=new CreateHoldRequestValidator().Validate(new CreateHoldRequest(Guid.NewGuid(),[id,id])); result.IsValid.Should().BeFalse(); }
}
