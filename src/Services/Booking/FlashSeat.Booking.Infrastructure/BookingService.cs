using FlashSeat.Booking.Application;
using FlashSeat.Booking.Domain;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace FlashSeat.Booking.Infrastructure;

public sealed class BookingService(BookingDbContext db, RedisSeatLock seatLock, EventsClient eventsClient, InventorySummaryService inventorySummary, TimeProvider timeProvider) : IBookingService
{
    public BookingService(BookingDbContext db, RedisSeatLock seatLock, EventsClient eventsClient, TimeProvider timeProvider)
        : this(db, seatLock, eventsClient, new InventorySummaryService(db, timeProvider), timeProvider) { }

    public async Task<IReadOnlyCollection<SeatAvailabilityResponse>> GetAvailabilityAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        return await db.Inventory.AsNoTracking().Where(x => x.EventId == eventId)
            .Select(x => new SeatAvailabilityResponse(x.SeatId,
                x.Status == SeatInventoryStatus.Held && x.HoldExpiresAt <= now ? "Available" : x.Status.ToString(),
                x.Status == SeatInventoryStatus.Held && x.HoldExpiresAt > now ? x.HoldExpiresAt : null))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<EventInventorySummaryResponse>> GetInventorySummariesAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken)
    {
        var ids = eventIds.Distinct().Take(100).ToArray();
        if (ids.Length == 0) return [];
        var now = timeProvider.GetUtcNow();
        var summaries = await db.InventorySummaries.AsNoTracking().Where(x => ids.Contains(x.EventId)).ToListAsync(cancellationToken);
        var expired = await db.Inventory.AsNoTracking().Where(x => ids.Contains(x.EventId) && x.Status == SeatInventoryStatus.Held && x.HoldExpiresAt <= now)
            .GroupBy(x => x.EventId).Select(x => new { EventId = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.EventId, cancellationToken);
        return summaries.Select(x =>
        {
            var expiredCount = expired.GetValueOrDefault(x.EventId)?.Count ?? 0;
            return new EventInventorySummaryResponse(x.EventId, x.TotalSeatCount, x.AvailableSeatCount + expiredCount, x.HeldSeatCount - expiredCount, x.BookedSeatCount, x.InventoryVersion, x.UpdatedAt);
        }).ToList();
    }

    public async Task<HoldAttemptResult> CreateHoldAsync(Guid userId, CreateHoldRequest request, CancellationToken cancellationToken)
    {
        EventSalesWindow? salesWindow;
        try { salesWindow = await eventsClient.GetSalesWindowAsync(request.EventId, cancellationToken); }
        catch (Exception exception) when (exception is HttpRequestException or NotSupportedException or JsonException)
        {
            return new(null, [], HoldAttemptFailure.SalesWindowUnavailable);
        }
        if (salesWindow is null || !salesWindow.IsOpen(timeProvider.GetUtcNow()))
            return new(null, [], HoldAttemptFailure.SalesNotOpen);

        var seatIds = request.SeatIds.Order().ToArray();
        await using var lease = await seatLock.AcquireAsync(request.EventId, seatIds);
        if (lease is null) return new(null, [], HoldAttemptFailure.LockContention);
        var now = timeProvider.GetUtcNow();
        if (!salesWindow.IsOpen(now)) return new(null, [], HoldAttemptFailure.SalesNotOpen);
        if (await db.Holds.AnyAsync(x => x.UserId == userId && x.EventId == request.EventId &&
            x.Status == SeatHoldStatus.Active && x.ExpiresAt > now, cancellationToken))
            return new(null, [], HoldAttemptFailure.ActiveHoldExists);

        var holdId = Guid.NewGuid();
        var expiresAt = now.AddMinutes(5);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var availableBeforeHold = await db.Inventory.CountAsync(x => x.EventId == request.EventId && seatIds.Contains(x.SeatId) && x.Status == SeatInventoryStatus.Available, cancellationToken);
        var affected = await db.Inventory
            .Where(x => x.EventId == request.EventId && seatIds.Contains(x.SeatId) &&
                (x.Status == SeatInventoryStatus.Available ||
                 x.Status == SeatInventoryStatus.Held && x.HoldExpiresAt <= now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, SeatInventoryStatus.Held)
                .SetProperty(x => x.HoldId, holdId)
                .SetProperty(x => x.HoldExpiresAt, expiresAt)
                .SetProperty(x => x.BookingId, (Guid?)null), cancellationToken);
        if (affected == seatIds.Length)
            await inventorySummary.ApplyDeltaAsync(request.EventId, -availableBeforeHold, availableBeforeHold, 0, cancellationToken);
        if (affected != seatIds.Length)
        {
            await transaction.RollbackAsync(cancellationToken);
            var unavailable = await db.Inventory.AsNoTracking().Where(x => x.EventId == request.EventId && seatIds.Contains(x.SeatId) &&
                !(x.Status == SeatInventoryStatus.Available || x.Status == SeatInventoryStatus.Held && x.HoldExpiresAt <= now))
                .Select(x => x.SeatId).ToListAsync(cancellationToken);
            return new(null, unavailable, HoldAttemptFailure.SeatsUnavailable);
        }

        var inventory = await db.Inventory.AsNoTracking().Where(x => x.EventId == request.EventId && seatIds.Contains(x.SeatId)).ToListAsync(cancellationToken);
        var hold = new SeatHold(holdId, userId, request.EventId, expiresAt, now);
        foreach (var seat in inventory) hold.Items.Add(new SeatHoldItem(holdId, seat.Id, seat.Price));
        db.Holds.Add(hold);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(ToHold(hold, inventory, now), []);
    }

    public async Task<HoldResponse?> GetHoldAsync(Guid userId, Guid holdId, CancellationToken cancellationToken)
    {
        var hold = await db.Holds.AsNoTracking().Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == holdId && x.UserId == userId, cancellationToken);
        if (hold is null) return null;
        var ids = hold.Items.Select(x => x.SeatInventoryId).ToArray();
        var inventory = await db.Inventory.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        return ToHold(hold, inventory, timeProvider.GetUtcNow());
    }

    public async Task<bool> ReleaseHoldAsync(Guid userId, Guid holdId, CancellationToken cancellationToken)
    {
        await using var lease = await seatLock.AcquireAsync(Guid.Empty, [holdId]);
        if (lease is null) return false;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var hold = await db.Holds.SingleOrDefaultAsync(x => x.Id == holdId && x.UserId == userId, cancellationToken);
        if (hold is null || hold.Status != SeatHoldStatus.Active)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }
        var inventory = await db.Inventory.Where(x => x.HoldId == holdId && x.Status == SeatInventoryStatus.Held && x.BookingId == null)
            .ToListAsync(cancellationToken);
        foreach (var seat in inventory) seat.Release(holdId);
        hold.Release();
        await inventorySummary.ApplyDeltaAsync(hold.EventId, inventory.Count, -inventory.Count, 0, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<BookingResponse?> CreateBookingAsync(Guid userId, CreateBookingRequest request, CancellationToken cancellationToken)
    {
        var existingEntity = await db.Bookings.AsNoTracking().Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.HoldId == request.HoldId && x.UserId == userId, cancellationToken);
        if (existingEntity is not null) return ToBooking(existingEntity);

        var existingAfterLock = await db.Bookings.AsNoTracking().Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.HoldId == request.HoldId && x.UserId == userId, cancellationToken);
        if (existingAfterLock is not null) return ToBooking(existingAfterLock);

        var holdSnapshot = await db.Holds.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == request.HoldId && x.UserId == userId, cancellationToken);
        if (holdSnapshot is null) return null;
        var eventMetadata = await eventsClient.GetMetadataAsync(holdSnapshot.EventId, cancellationToken);
        if (eventMetadata is null || eventMetadata.IsArchived || eventMetadata.Status != "Published") return null;

        var now = timeProvider.GetUtcNow();
        if (eventMetadata.EndsAt <= now) return null;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var hold = await db.Holds.Include(x => x.Items).SingleOrDefaultAsync(
            x => x.Id == request.HoldId && x.UserId == userId, cancellationToken);
        if (hold is null || hold.Status != SeatHoldStatus.Active || hold.ExpiresAt <= now)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }
        var ids = hold.Items.Select(x => x.SeatInventoryId).ToArray();
        var inventory = await db.Inventory.Where(x => ids.Contains(x.Id) && x.HoldId == hold.Id && x.Status == SeatInventoryStatus.Held).ToListAsync(cancellationToken);
        if (inventory.Count != ids.Length)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }
        var entity = new global::FlashSeat.Booking.Domain.Booking(Guid.NewGuid(), $"FS-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            userId, hold.EventId, hold.Id, inventory.Sum(x => x.Price), inventory[0].Currency, now);
        entity.SetEventSnapshot(new EventSnapshot(eventMetadata.Name, eventMetadata.Slug, eventMetadata.Description,
            eventMetadata.ImageUrl, eventMetadata.VenueName, eventMetadata.Address, eventMetadata.StartsAt,
            eventMetadata.EndsAt, eventMetadata.Status));
        foreach (var seat in inventory)
        {
            entity.Items.Add(new BookingItem(Guid.NewGuid(), entity.Id, seat.SeatId, seat.Section, seat.Row, seat.Number,
                seat.Price, seat.Currency, TicketCodeGenerator.Create()));
            seat.AssignBooking(hold.Id, entity.Id);
        }
        hold.Convert();
        db.Bookings.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToBooking(entity);
    }

    public async Task<BookingResponse?> GetBookingAsync(Guid userId, bool isAdmin, Guid bookingId, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.AsNoTracking().Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == bookingId && (isAdmin || x.UserId == userId), cancellationToken);
        return booking is null ? null : ToBooking(booking);
    }

    public async Task<IReadOnlyCollection<BookingResponse>> GetBookingsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var bookings = await db.Bookings.AsNoTracking().Include(x => x.Items)
            .Where(x => x.UserId == userId).OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
        return bookings.Select(ToBooking).ToList();
    }

    public async Task ImportInventoryAsync(InventoryImportRequest request, CancellationToken cancellationToken)
    {
        var existing = await db.Inventory.Where(x => x.EventId == request.EventId).Select(x => x.SeatId).ToListAsync(cancellationToken);
        foreach (var seat in request.Seats.Where(x => !existing.Contains(x.SeatId)))
            db.Inventory.Add(new EventSeatInventory(Guid.NewGuid(), request.EventId, seat.SeatId, seat.Section, seat.Row, seat.Number, seat.Price, seat.Currency));
        await db.SaveChangesAsync(cancellationToken);
        await inventorySummary.RebuildAsync(request.EventId, cancellationToken);
    }

    public async Task<EventActivityResponse> GetEventActivityAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var activeHoldCount = await db.Holds.CountAsync(x => x.EventId == eventId && x.Status == SeatHoldStatus.Active && x.ExpiresAt > timeProvider.GetUtcNow(), cancellationToken);
        var pendingBookingCount = await db.Bookings.CountAsync(x => x.EventId == eventId && x.Status == BookingStatus.PendingPayment, cancellationToken);
        var hasHistoricalActivity = await db.Holds.AnyAsync(x => x.EventId == eventId, cancellationToken)
            || await db.Bookings.AnyAsync(x => x.EventId == eventId, cancellationToken);
        return new EventActivityResponse(eventId, hasHistoricalActivity, activeHoldCount, pendingBookingCount);
    }

    public async Task ReplaceInventoryAsync(InventoryReplacementRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await db.Holds.AnyAsync(x => x.EventId == request.EventId, cancellationToken)
            || await db.Bookings.AnyAsync(x => x.EventId == request.EventId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException("Event inventory cannot be replaced after booking activity.");
        }

        await db.Inventory.Where(x => x.EventId == request.EventId).ExecuteDeleteAsync(cancellationToken);
        foreach (var seat in request.Seats)
            db.Inventory.Add(new EventSeatInventory(Guid.NewGuid(), request.EventId, seat.SeatId, seat.Section, seat.Row, seat.Number, seat.Price, seat.Currency));
        await db.SaveChangesAsync(cancellationToken);
        await inventorySummary.RebuildAsync(request.EventId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<CheckInAttemptResult> CheckInAsync(Guid operatorId, string ticketCode, CancellationToken cancellationToken)
    {
        if (!TicketCodeGenerator.TryParse(ticketCode.Trim(), out var normalized))
            return new(null, CheckInFailure.UnknownTicket);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var ticketId = await db.BookingItems.AsNoTracking()
            .Where(x => x.TicketCode == normalized)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (ticketId is null) return new(null, CheckInFailure.UnknownTicket);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM booking_items WHERE \"Id\" = {ticketId.Value} FOR UPDATE",
            cancellationToken);
        var ticket = await db.BookingItems.Include(x => x.Booking)
            .SingleAsync(x => x.Id == ticketId.Value, cancellationToken);
        if (ticket.Booking.Status != BookingStatus.Confirmed)
            return new(null, CheckInFailure.BookingNotConfirmed);
        if (ticket.CheckInStatus == TicketCheckInStatus.CheckedIn)
            return new(ToCheckIn(ticket), CheckInFailure.AlreadyCheckedIn);

        ticket.CheckIn(operatorId, timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(ToCheckIn(ticket));
    }

    private static HoldResponse ToHold(SeatHold hold, IReadOnlyCollection<EventSeatInventory> inventory, DateTimeOffset now) =>
        new(hold.Id, hold.EventId, hold.ExpiresAt <= now && hold.Status == SeatHoldStatus.Active ? SeatHoldStatus.Expired.ToString() : hold.Status.ToString(), hold.ExpiresAt,
            inventory.Select(x => new HoldItemResponse(x.SeatId, x.Section, x.Row, x.Number, x.Price)).ToList(),
            inventory.Sum(x => x.Price), inventory.FirstOrDefault()?.Currency ?? "VND");
    private static BookingResponse ToBooking(global::FlashSeat.Booking.Domain.Booking x) => new(x.Id, x.BookingNumber, x.EventId, x.Status.ToString(), x.TotalAmount, x.Currency, x.CreatedAt, x.ConfirmedAt,
        x.EventSnapshotAvailable ? ToEvent(x) : null,
        x.Items.OrderBy(i => i.Section).ThenBy(i => i.Row).ThenBy(i => i.Number).Select(ToItem).ToList());
    private static BookingEventResponse ToEvent(global::FlashSeat.Booking.Domain.Booking x) =>
        new(x.EventId, x.EventName, x.EventSlug, x.EventDescription, x.EventImageUrl, x.EventVenueName, x.EventAddress, x.EventStartsAt, x.EventEndsAt, x.EventStatus);
    private static BookingItemResponse ToItem(BookingItem i) => new(i.Id, i.SeatId, i.Section, i.Row, i.Number, i.Price, i.Currency, i.TicketCode, i.CheckInStatus.ToString(), i.CheckedInAt, i.CheckedInBy);
    private static CheckInResponse ToCheckIn(BookingItem item) => new(item.TicketCode, item.CheckInStatus.ToString(), item.CheckedInAt,
        item.Booking.BookingNumber, item.Booking.EventSnapshotAvailable ? ToEvent(item.Booking) : null, ToItem(item));
}

public sealed class EventsClient(HttpClient client)
{
    public async Task<EventSalesWindow?> GetSalesWindowAsync(Guid eventId, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"/api/events/{eventId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<EventSalesWindow>(cancellationToken)
            ?? throw new HttpRequestException("Events service returned an invalid sales window.");
    }

    public async Task<EventMetadataResponse?> GetMetadataAsync(Guid eventId, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"/internal/events/{eventId}/metadata", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<EventMetadataResponse>(cancellationToken)
            ?? throw new HttpRequestException("Events service returned invalid event metadata.");
    }
}

public sealed record EventSalesWindow(DateTimeOffset SalesStartAt, DateTimeOffset SalesEndAt)
{
    public bool IsOpen(DateTimeOffset now) => SalesStartAt <= now && now < SalesEndAt;
}
