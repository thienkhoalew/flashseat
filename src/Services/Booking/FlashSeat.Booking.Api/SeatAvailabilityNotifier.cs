using FlashSeat.Booking.Application;
using Microsoft.AspNetCore.SignalR;

namespace FlashSeat.Booking.Api;

public sealed class SeatAvailabilityNotifier(IHubContext<SeatAvailabilityHub> hub) : ISeatAvailabilityNotifier
{
    public Task NotifyAsync(Guid eventId, IReadOnlyCollection<Guid> seatIds, string status, CancellationToken cancellationToken) =>
        hub.Clients.Group($"event:{eventId:N}").SendAsync(
            status == "Booked" ? "SeatsBooked" : "SeatsReleased",
            new { eventId, seatIds, status, timestamp = DateTimeOffset.UtcNow },
            cancellationToken);
}
