using FlashSeat.Events.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlashSeat.Events.Infrastructure;

public static class EventsSeeder
{
    private static readonly Fixture[] Fixtures =
    [
        new("Midnight Echoes", "A late-night electronic show with immersive lighting.", "FlashSeat Arena", "12 Riverfront Avenue", true),
        new("City Lights Orchestra", "A modern orchestral performance inspired by the city after dark.", "Grand City Theatre", "88 Central Boulevard", true),
        new("Neon Pulse Festival", "An all-day festival featuring electronic artists and visual installations.", "FlashSeat Arena", "12 Riverfront Avenue", true),
        new("Acoustic Stories", "An intimate evening of acoustic songs and stories.", "Lantern Hall", "24 Garden Street", false),
        new("Indie Horizon", "Emerging indie bands perform live from across the region.", "Warehouse 9", "9 Harbor Road", true),
        new("Laugh Track Live", "A fast-paced stand-up comedy night with four headline performers.", "Grand City Theatre", "88 Central Boulevard", true),
        new("Symphony of Games", "Iconic game soundtracks performed by a full orchestra.", "Harmony Concert Hall", "30 Arts District", true),
        new("Future Makers Summit", "A practical conference for product builders, engineers, and designers.", "Metro Convention Center", "101 Innovation Way", true),
        new("Street Dance Finals", "Top dance crews compete for the national championship.", "FlashSeat Arena", "12 Riverfront Avenue", true),
        new("Jazz Under the Stars", "An open-air jazz program featuring local and international artists.", "Riverside Park", "5 Moonlight Walk", true),
        new("Culinary Stage", "Live cooking demonstrations from award-winning chefs.", "Metro Convention Center", "101 Innovation Way", false),
        new("Theatre Lab: New Voices", "A staged reading series for original plays by emerging writers.", "Lantern Hall", "24 Garden Street", false)
    ];

    public static async Task SeedEventsAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EventsDbContext>();
        await db.Database.EnsureCreatedAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        for (var index = 0; index < Fixtures.Length; index++)
        {
            var fixture = Fixtures[index];
            var slug = $"flashseat-live-{index + 1}";
            var entity = await db.Events.SingleOrDefaultAsync(x => x.Slug == slug, cancellationToken);
            if (entity is not null)
            {
                db.Entry(entity).Property(x => x.Name).CurrentValue = fixture.Name;
                db.Entry(entity).Property(x => x.Description).CurrentValue = fixture.Description;
                db.Entry(entity).Property(x => x.VenueName).CurrentValue = fixture.VenueName;
                db.Entry(entity).Property(x => x.Address).CurrentValue = fixture.Address;
                continue;
            }

            var startsAt = now.AddDays((index + 1) * 14);
            entity = new EventEntity(Guid.NewGuid(), fixture.Name, slug, fixture.Description,
                $"https://images.unsplash.com/photo-1501386761578-eac5c94b800a?auto=format&fit=crop&w=1600&q=80&sig={index + 1}",
                fixture.VenueName, fixture.Address, startsAt, now.AddDays(-1), startsAt.AddDays(-1), now);
            AddSeats(entity);
            if (fixture.Published) entity.Publish(now);
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

    private sealed record Fixture(string Name, string Description, string VenueName, string Address, bool Published);
}
