using FlashSeat.Booking.Domain;
using Microsoft.EntityFrameworkCore;

namespace FlashSeat.Booking.Infrastructure;

public sealed class InventorySummaryService(BookingDbContext db, TimeProvider timeProvider)
{
    public async Task ApplyDeltaAsync(Guid eventId, int availableDelta, int heldDelta, int bookedDelta, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var affected = await db.InventorySummaries
            .Where(x => x.EventId == eventId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.AvailableSeatCount, x => x.AvailableSeatCount + availableDelta)
                .SetProperty(x => x.HeldSeatCount, x => x.HeldSeatCount + heldDelta)
                .SetProperty(x => x.BookedSeatCount, x => x.BookedSeatCount + bookedDelta)
                .SetProperty(x => x.UpdatedAt, now)
                .SetProperty(x => x.InventoryVersion, x => x.InventoryVersion + 1), cancellationToken);

        if (affected == 0)
            await RebuildAsync(eventId, cancellationToken);
    }

    public async Task RebuildAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var counts = await db.Inventory.AsNoTracking()
            .Where(x => x.EventId == eventId)
            .GroupBy(x => 1)
            .Select(x => new
            {
                Total = x.Count(),
                Available = x.Count(s => s.Status == SeatInventoryStatus.Available || (s.Status == SeatInventoryStatus.Held && s.HoldExpiresAt <= now)),
                Held = x.Count(s => s.Status == SeatInventoryStatus.Held && s.HoldExpiresAt > now),
                Booked = x.Count(s => s.Status == SeatInventoryStatus.Booked)
            })
            .SingleOrDefaultAsync(cancellationToken) ?? new { Total = 0, Available = 0, Held = 0, Booked = 0 };

        var summary = await db.InventorySummaries.SingleOrDefaultAsync(x => x.EventId == eventId, cancellationToken);
        if (summary is null)
            db.InventorySummaries.Add(new EventInventorySummary(eventId, counts.Total, counts.Available, counts.Held, counts.Booked, now));
        else
            summary.ReplaceCounts(counts.Total, counts.Available, counts.Held, counts.Booked, now);
        await db.SaveChangesAsync(cancellationToken);
    }
}
