using System.Net.Http.Json;
using FlashSeat.Events.Domain;

namespace FlashSeat.Events.Infrastructure;

public sealed class BookingInventoryClient(HttpClient client)
{
    public async Task ImportAsync(EventEntity entity, CancellationToken cancellationToken)
    {
        var request = new
        {
            eventId = entity.Id,
            seats = entity.Seats.Select(x => new
            {
                seatId = x.Id,
                x.Section,
                x.Row,
                x.Number,
                x.Price,
                x.Currency
            })
        };
        using var response = await client.PostAsJsonAsync("/internal/events/inventory", request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
