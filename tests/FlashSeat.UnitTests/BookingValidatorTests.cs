using FlashSeat.Booking.Application;
using FlashSeat.Booking.Domain;
using FluentAssertions;
using Xunit;
namespace FlashSeat.UnitTests;
public sealed class BookingValidatorTests
{
    [Fact] public void Hold_rejects_more_than_six_seats()
    { var result=new CreateHoldRequestValidator().Validate(new CreateHoldRequest(Guid.NewGuid(),Enumerable.Range(0,7).Select(_=>Guid.NewGuid()).ToArray())); result.IsValid.Should().BeFalse(); }
    [Fact] public void Hold_rejects_duplicate_seats()
    { var id=Guid.NewGuid(); var result=new CreateHoldRequestValidator().Validate(new CreateHoldRequest(Guid.NewGuid(),[id,id])); result.IsValid.Should().BeFalse(); }
    [Fact] public void Pending_booking_expires()
    { var booking=new FlashSeat.Booking.Domain.Booking(Guid.NewGuid(),"FS-1",Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),100,"VND",DateTimeOffset.UtcNow); booking.Expire(); booking.Status.Should().Be(BookingStatus.Expired); }
    [Fact] public void Confirmed_booking_does_not_expire()
    { var booking=new FlashSeat.Booking.Domain.Booking(Guid.NewGuid(),"FS-1",Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),100,"VND",DateTimeOffset.UtcNow); booking.Confirm(Guid.NewGuid(),DateTimeOffset.UtcNow); booking.Expire(); booking.Status.Should().Be(BookingStatus.Confirmed); }
    [Fact] public void Inventory_release_requires_current_hold_and_booking()
    { var holdId=Guid.NewGuid(); var bookingId=Guid.NewGuid(); var inventory=new EventSeatInventory(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"Main","A",1,100,"VND"); inventory.Hold(holdId,DateTimeOffset.UtcNow.AddMinutes(5)); inventory.AssignBooking(holdId,bookingId); inventory.Release(Guid.NewGuid(),bookingId); inventory.Status.Should().Be(SeatInventoryStatus.Held); inventory.Release(holdId,bookingId); inventory.Status.Should().Be(SeatInventoryStatus.Available); }
}
