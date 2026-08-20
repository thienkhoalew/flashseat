using System.Net;
using System.Text.Json;
using FlashSeat.Booking.Application;
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
            ALTER TABLE bookings ADD COLUMN IF NOT EXISTS "EventName" text NOT NULL DEFAULT '';
            ALTER TABLE bookings ADD COLUMN IF NOT EXISTS "EventSlug" text NOT NULL DEFAULT '';
            ALTER TABLE bookings ADD COLUMN IF NOT EXISTS "EventDescription" text NOT NULL DEFAULT '';
            ALTER TABLE bookings ADD COLUMN IF NOT EXISTS "EventImageUrl" text NOT NULL DEFAULT '';
            ALTER TABLE bookings ADD COLUMN IF NOT EXISTS "EventVenueName" text NOT NULL DEFAULT '';
            ALTER TABLE bookings ADD COLUMN IF NOT EXISTS "EventAddress" text NOT NULL DEFAULT '';
            ALTER TABLE bookings ADD COLUMN IF NOT EXISTS "EventStatus" text NOT NULL DEFAULT '';
            ALTER TABLE bookings ADD COLUMN IF NOT EXISTS "EventSnapshotAvailable" boolean NOT NULL DEFAULT false;
            ALTER TABLE bookings ADD COLUMN IF NOT EXISTS "EventStartsAt" timestamp with time zone NOT NULL DEFAULT NOW();
            ALTER TABLE bookings ADD COLUMN IF NOT EXISTS "EventEndsAt" timestamp with time zone NOT NULL DEFAULT NOW();
            ALTER TABLE booking_items ADD COLUMN IF NOT EXISTS "Currency" text NOT NULL DEFAULT 'VND';
            ALTER TABLE booking_items ADD COLUMN IF NOT EXISTS "TicketCode" text;
            ALTER TABLE booking_items ADD COLUMN IF NOT EXISTS "CheckInStatus" text NOT NULL DEFAULT 'NotCheckedIn';
            ALTER TABLE booking_items ADD COLUMN IF NOT EXISTS "CheckedInAt" timestamp with time zone;
            ALTER TABLE booking_items ADD COLUMN IF NOT EXISTS "CheckedInBy" uuid;
            """, cancellationToken);
        var tickets = await db.Database.SqlQueryRaw<TicketCodeBackfillRow>(
            "SELECT \"Id\", \"TicketCode\" FROM booking_items WHERE \"TicketCode\" IS NULL OR \"TicketCode\" = '' OR \"TicketCode\" LIKE 'FS1-%'")
            .ToListAsync(cancellationToken);
        var legacyValues = tickets.Select(x => x.TicketCode).Where(x => !string.IsNullOrEmpty(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usedCodes = (await db.BookingItems.AsNoTracking()
            .Where(x => x.TicketCode != null && x.TicketCode != "")
            .Select(x => x.TicketCode)
            .ToListAsync(cancellationToken))
            .Where(x => x is not null && !legacyValues.Contains(x))
            .Select(x => x!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in tickets)
        {
            var candidate = item.TicketCode?.StartsWith("FS1-", StringComparison.OrdinalIgnoreCase) == true &&
                            item.TicketCode.Length == 36 && item.TicketCode[4..].All(Uri.IsHexDigit)
                ? item.TicketCode[4..].ToUpperInvariant()
                : TicketCodeGenerator.Create();
            while (!usedCodes.Add(candidate)) candidate = TicketCodeGenerator.Create();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE booking_items SET \"TicketCode\" = {candidate} WHERE \"Id\" = {item.Id}",
                cancellationToken);
        }
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE booking_items ALTER COLUMN \"TicketCode\" SET NOT NULL;", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_booking_items_TicketCode\" ON booking_items (\"TicketCode\");", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await BackfillEventSnapshotsAsync(db, scope.ServiceProvider.GetRequiredService<EventsClient>(), cancellationToken);
    }

    private static async Task BackfillEventSnapshotsAsync(BookingDbContext db, EventsClient eventsClient, CancellationToken cancellationToken)
    {
        var bookings = await db.Bookings.AsNoTracking()
            .Where(x => !x.EventSnapshotAvailable)
            .Select(x => new { x.EventId })
            .Distinct()
            .ToListAsync(cancellationToken);
        foreach (var booking in bookings)
        {
            EventMetadataResponse? metadata;
            try
            {
                metadata = await eventsClient.GetMetadataAsync(booking.EventId, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException)
            {
                continue;
            }
            if (metadata is null) continue;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE bookings
                SET "EventName" = {metadata.Name},
                    "EventSlug" = {metadata.Slug},
                    "EventDescription" = {metadata.Description},
                    "EventImageUrl" = {metadata.ImageUrl},
                    "EventVenueName" = {metadata.VenueName},
                    "EventAddress" = {metadata.Address},
                    "EventStartsAt" = {metadata.StartsAt},
                    "EventEndsAt" = {metadata.EndsAt},
                    "EventStatus" = {metadata.Status},
                    "EventSnapshotAvailable" = TRUE
                WHERE "EventId" = {booking.EventId}
                  AND "EventSnapshotAvailable" = FALSE
                """, cancellationToken);
        }
    }

    private sealed class TicketCodeBackfillRow
    {
        public Guid Id { get; init; }
        public string? TicketCode { get; init; }
    }
}
