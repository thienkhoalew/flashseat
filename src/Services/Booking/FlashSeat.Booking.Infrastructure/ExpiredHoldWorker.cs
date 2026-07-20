using FlashSeat.Booking.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlashSeat.Booking.Infrastructure;

public sealed class ExpiredHoldWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider,
    ILogger<ExpiredHoldWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15), timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken)) await ReleaseBatchAsync(stoppingToken);
    }

    private async Task ReleaseBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var now = timeProvider.GetUtcNow();
        var holds = await db.Holds.Where(x => x.ExpiresAt <= now &&
                (x.Status == SeatHoldStatus.Active || x.Status == SeatHoldStatus.Converted))
            .OrderBy(x => x.ExpiresAt).Take(100).ToListAsync(cancellationToken);
        if (holds.Count == 0) return;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var hold in holds)
        {
            Guid? bookingId = null;
            if (hold.Status == SeatHoldStatus.Active) hold.Expire();
            else
            {
                var booking = await db.Bookings.SingleOrDefaultAsync(x => x.HoldId == hold.Id && x.Status == BookingStatus.PendingPayment, cancellationToken);
                if (booking is null) continue;
                booking.Expire();
                hold.Expire();
                bookingId = booking.Id;
            }
            await db.Inventory.Where(x => x.HoldId == hold.Id && x.BookingId == bookingId && x.Status == SeatInventoryStatus.Held)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, SeatInventoryStatus.Available)
                    .SetProperty(x => x.HoldId, (Guid?)null)
                    .SetProperty(x => x.HoldExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(x => x.BookingId, (Guid?)null), cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Released {ExpiredHoldCount} expired seat holds", holds.Count);
    }
}
