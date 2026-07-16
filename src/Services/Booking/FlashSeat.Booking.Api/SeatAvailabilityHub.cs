using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FlashSeat.Booking.Api;

[Authorize]
public sealed class SeatAvailabilityHub : Hub
{
    public Task JoinEvent(Guid eventId) => Groups.AddToGroupAsync(Context.ConnectionId, $"event:{eventId:N}");
    public Task LeaveEvent(Guid eventId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"event:{eventId:N}");
}
