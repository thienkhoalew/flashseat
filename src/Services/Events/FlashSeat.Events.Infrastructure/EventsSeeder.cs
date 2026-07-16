using FlashSeat.Events.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlashSeat.Events.Infrastructure;

public static class EventsSeeder
{
    public static async Task SeedEventsAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EventsDbContext>();
        await db.Database.EnsureCreatedAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        for (var index = 1; index <= 4; index++)
        {
            var slug = $"flashseat-live-{index}";
            if (await db.Events.AnyAsync(x => x.Slug == slug, cancellationToken)) continue;
            var startsAt = now.AddDays(index * 14);
            var entity = new EventEntity(Guid.NewGuid(), $"FlashSeat Live {index}", slug,
                "Đêm nhạc trực tiếp với trải nghiệm ghế ngồi cao cấp.",
                $"https://images.unsplash.com/photo-1501386761578-eac5c94b800a?auto=format&fit=crop&w=1600&q=80&sig={index}",
                index % 2 == 0 ? "Nhà hát Thành phố" : "FlashSeat Arena", "Quận 1, TP. Hồ Chí Minh",
                startsAt, now.AddDays(-1), startsAt.AddDays(-1), now);
            AddSeats(entity);
            if (index <= 3) entity.Publish(now);
            db.Events.Add(entity);
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void AddSeats(EventEntity entity)
    {
        var sections = new[] { ("VIP", 900_000m), ("Standard", 550_000m), ("Economy", 300_000m) };
        foreach (var (section, price) in sections)
            foreach (var row in new[] { "A", "B", "C" })
                for (var number = 1; number <= 10; number++)
                    entity.Seats.Add(new Seat(Guid.NewGuid(), entity.Id, section, row, number, price, "VND"));
    }
}
