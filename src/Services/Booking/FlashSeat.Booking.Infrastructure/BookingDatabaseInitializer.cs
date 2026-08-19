using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlashSeat.Booking.Infrastructure;

public static class BookingDatabaseInitializer
{
    public static async Task InitializeBookingDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        await db.Database.EnsureCreatedAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtext('flashseat_booking_inventory_summary'));", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS event_inventory_summary (
                "EventId" uuid PRIMARY KEY,
                "TotalSeatCount" integer NOT NULL,
                "AvailableSeatCount" integer NOT NULL,
                "HeldSeatCount" integer NOT NULL,
                "BookedSeatCount" integer NOT NULL,
                "InventoryVersion" bigint NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL
            );
            INSERT INTO event_inventory_summary ("EventId", "TotalSeatCount", "AvailableSeatCount", "HeldSeatCount", "BookedSeatCount", "InventoryVersion", "UpdatedAt")
            SELECT "EventId",
                   COUNT(*)::integer,
                   COUNT(*) FILTER (WHERE "Status" = 'Available' OR ("Status" = 'Held' AND "HoldExpiresAt" <= NOW()))::integer,
                   COUNT(*) FILTER (WHERE "Status" = 'Held' AND "HoldExpiresAt" > NOW())::integer,
                   COUNT(*) FILTER (WHERE "Status" = 'Booked')::integer,
                   1,
                   NOW()
            FROM event_seat_inventory
            GROUP BY "EventId"
            ON CONFLICT ("EventId") DO NOTHING;
            """, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
