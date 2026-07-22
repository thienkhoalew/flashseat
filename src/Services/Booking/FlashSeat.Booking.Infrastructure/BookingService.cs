using FlashSeat.Booking.Application;
using FlashSeat.Booking.Domain;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace FlashSeat.Booking.Infrastructure;

public sealed class BookingService(BookingDbContext db, RedisSeatLock seatLock, EventsClient eventsClient, TimeProvider timeProvider) : IBookingService
{
    public async Task<IReadOnlyCollection<SeatAvailabilityResponse>> GetAvailabilityAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        return await db.Inventory.AsNoTracking().Where(x => x.EventId == eventId)
            .Select(x => new SeatAvailabilityResponse(x.SeatId,
                x.Status == SeatInventoryStatus.Held && x.HoldExpiresAt <= now ? "Available" : x.Status.ToString(),
                x.Status == SeatInventoryStatus.Held && x.HoldExpiresAt > now ? x.HoldExpiresAt : null))
            .ToListAsync(cancellationToken);
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
        var affected = await db.Inventory
            .Where(x => x.EventId == request.EventId && seatIds.Contains(x.SeatId) &&
                (x.Status == SeatInventoryStatus.Available ||
                 x.Status == SeatInventoryStatus.Held && x.HoldExpiresAt <= now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, SeatInventoryStatus.Held)
                .SetProperty(x => x.HoldId, holdId)
                .SetProperty(x => x.HoldExpiresAt, expiresAt)
                .SetProperty(x => x.BookingId, (Guid?)null), cancellationToken);
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
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<BookingResponse?> CreateBookingAsync(Guid userId, CreateBookingRequest request, CancellationToken cancellationToken)
    {
        var existing = await db.Bookings.AsNoTracking().Where(x => x.HoldId == request.HoldId && x.UserId == userId)
            .Select(ToBookingExpression()).SingleOrDefaultAsync(cancellationToken);
        if (existing is not null) return existing;

        await using var lease = await seatLock.AcquireAsync(Guid.Empty, [request.HoldId]);
        if (lease is null) return null;
        existing = await db.Bookings.AsNoTracking().Where(x => x.HoldId == request.HoldId && x.UserId == userId)
            .Select(ToBookingExpression()).SingleOrDefaultAsync(cancellationToken);
        if (existing is not null) return existing;

        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var hold = await db.Holds.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == request.HoldId && x.UserId == userId, cancellationToken);
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
        foreach (var seat in inventory)
        {
            entity.Items.Add(new BookingItem(Guid.NewGuid(), entity.Id, seat.SeatId, seat.Section, seat.Row, seat.Number, seat.Price));
            seat.AssignBooking(hold.Id, entity.Id);
        }
        hold.Convert();
        db.Bookings.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToBooking(entity);
    }

    public async Task<BookingResponse?> GetBookingAsync(Guid userId, bool isAdmin, Guid bookingId, CancellationToken cancellationToken) =>
        await db.Bookings.AsNoTracking().Where(x => x.Id == bookingId && (isAdmin || x.UserId == userId)).Select(ToBookingExpression()).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyCollection<BookingResponse>> GetBookingsAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Bookings.AsNoTracking().Where(x => x.UserId == userId).OrderByDescending(x => x.CreatedAt).Select(ToBookingExpression()).ToListAsync(cancellationToken);

    public async Task ImportInventoryAsync(InventoryImportRequest request, CancellationToken cancellationToken)
    {
        var existing = await db.Inventory.Where(x => x.EventId == request.EventId).Select(x => x.SeatId).ToListAsync(cancellationToken);
        foreach (var seat in request.Seats.Where(x => !existing.Contains(x.SeatId)))
            db.Inventory.Add(new EventSeatInventory(Guid.NewGuid(), request.EventId, seat.SeatId, seat.Section, seat.Row, seat.Number, seat.Price, seat.Currency));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static HoldResponse ToHold(SeatHold hold, IReadOnlyCollection<EventSeatInventory> inventory, DateTimeOffset now) =>
        new(hold.Id, hold.EventId, hold.ExpiresAt <= now && hold.Status == SeatHoldStatus.Active ? SeatHoldStatus.Expired.ToString() : hold.Status.ToString(), hold.ExpiresAt,
            inventory.Select(x => new HoldItemResponse(x.SeatId, x.Section, x.Row, x.Number, x.Price)).ToList(),
            inventory.Sum(x => x.Price), inventory.FirstOrDefault()?.Currency ?? "VND");
    private static BookingResponse ToBooking(global::FlashSeat.Booking.Domain.Booking x) => new(x.Id, x.BookingNumber, x.EventId, x.Status.ToString(), x.TotalAmount, x.Currency, x.CreatedAt,
        x.Items.Select(i => new BookingItemResponse(i.SeatId, i.Section, i.Row, i.Number, i.Price)).ToList());
    private static System.Linq.Expressions.Expression<Func<global::FlashSeat.Booking.Domain.Booking, BookingResponse>> ToBookingExpression() => x =>
        new BookingResponse(x.Id, x.BookingNumber, x.EventId, x.Status.ToString(), x.TotalAmount, x.Currency, x.CreatedAt,
            x.Items.Select(i => new BookingItemResponse(i.SeatId, i.Section, i.Row, i.Number, i.Price)).ToList());
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
}

public sealed record EventSalesWindow(DateTimeOffset SalesStartAt, DateTimeOffset SalesEndAt)
{
    public bool IsOpen(DateTimeOffset now) => SalesStartAt <= now && now < SalesEndAt;
}
