using System.Net.Http.Json;
using FlashSeat.Events.Domain;

namespace FlashSeat.Events.Infrastructure;

public sealed class BookingInventoryClient(HttpClient client)
{
    public async Task<EventActivity> GetActivityAsync(Guid eventId, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"/internal/events/{eventId}/activity", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<EventActivity>(cancellationToken)
            ?? throw new HttpRequestException("Booking service returned invalid event activity.");
    }

    public async Task ReplaceAsync(EventEntity entity, CancellationToken cancellationToken)
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
        using var response = await client.PutAsJsonAsync("/internal/events/inventory", request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

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

    public async Task<IReadOnlyCollection<InventorySummary>> GetSummariesAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken)
    {
        if (eventIds.Count == 0) return [];
        using var response = await client.PostAsJsonAsync("/internal/events/inventory-summary", new { eventIds }, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyCollection<InventorySummary>>(cancellationToken) ?? [];
    }
}

public sealed record EventActivity(Guid EventId, bool HasHistoricalActivity, int ActiveHoldCount, int PendingBookingCount);

public sealed record InventorySummary(Guid EventId, int TotalSeatCount, int AvailableSeatCount, int HeldSeatCount,
    int BookedSeatCount, long InventoryVersion, DateTimeOffset AvailabilityAsOf)
{
    public string AvailabilityStatus => AvailableSeatCount == 0 ? "SoldOut" : "Available";
}
