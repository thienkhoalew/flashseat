using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlashSeat.Booking.Infrastructure;

public static class DemoInventorySeeder
{
    public static async Task SyncDemoInventoryAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        if (await db.Inventory.AnyAsync(cancellationToken)) return;

        using var client = new HttpClient { BaseAddress = new Uri("http://events-api:8080") };
        var events = await client.GetFromJsonAsync<EventPage>("/api/events?pageSize=50", cancellationToken);
        if (events is null) return;
        foreach (var item in events.Items)
        {
            var detail = await client.GetFromJsonAsync<EventDetail>($"/api/events/{item.Id}", cancellationToken);
            if (detail is null) continue;
            foreach (var seat in detail.Seats)
                db.Inventory.Add(new Domain.EventSeatInventory(Guid.NewGuid(), detail.Id, seat.Id, seat.Section, seat.Row, seat.Number, seat.Price, seat.Currency));
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private sealed record EventPage(IReadOnlyCollection<EventItem> Items);
    private sealed record EventItem(Guid Id);
    private sealed record EventDetail(Guid Id, IReadOnlyCollection<EventSeat> Seats);
    private sealed record EventSeat(Guid Id, string Section, string Row, int Number, decimal Price, string Currency);
}
