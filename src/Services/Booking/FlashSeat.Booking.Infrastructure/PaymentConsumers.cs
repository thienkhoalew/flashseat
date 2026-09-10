using FlashSeat.Booking.Domain;
using FlashSeat.Contracts;
using FlashSeat.Booking.Application;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace FlashSeat.Booking.Infrastructure;

public sealed class PaymentSucceededConsumer(BookingDbContext db, InventorySummaryService inventorySummary, TimeProvider timeProvider, EventsClient eventsClient, ISeatAvailabilityNotifier availabilityNotifier) : IConsumer<PaymentSucceededV1>
{
    public async Task Consume(ConsumeContext<PaymentSucceededV1> context)
    {
        var message = context.Message;
        var booking = await db.Bookings.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == message.BookingId, context.CancellationToken);
        if (booking is null || booking.Status != BookingStatus.PendingPayment || booking.UserId != message.UserId ||
            booking.TotalAmount != message.Amount || booking.Currency != message.Currency) return;
        var metadata = await eventsClient.GetMetadataAsync(booking.EventId, context.CancellationToken);
        if (metadata is null || metadata.IsArchived || metadata.Status != "Published" || metadata.EndsAt <= timeProvider.GetUtcNow()) return;
        var hold = await db.Holds.SingleAsync(x => x.Id == booking.HoldId, context.CancellationToken);
        if (hold.ExpiresAt < message.OccurredAt) return;
        var inventory = await db.Inventory.Where(x => x.HoldId == hold.Id && x.BookingId == booking.Id && x.Status == SeatInventoryStatus.Held)
            .ToListAsync(context.CancellationToken);
        if (inventory.Count != booking.Items.Count) return;
        booking.Confirm(message.PaymentId, timeProvider.GetUtcNow());
        foreach (var seat in inventory) seat.Book(booking.Id);
        await inventorySummary.ApplyDeltaAsync(booking.EventId, 0, -inventory.Count, inventory.Count, context.CancellationToken);
        await db.SaveChangesAsync(context.CancellationToken);
        await availabilityNotifier.NotifyAsync(booking.EventId, inventory.Select(x => x.SeatId).ToArray(), "Booked", context.CancellationToken);

        var tickets = booking.Items.Select(x => new ConfirmedTicketItemV1(
            x.Section, x.Row, x.Number, x.Price, x.Currency, x.TicketCode)).ToList();

        await context.Publish(new BookingConfirmedV1(
            Guid.NewGuid(),
            message.CorrelationId,
            timeProvider.GetUtcNow(),
            1,
            booking.Id,
            booking.UserId,
            booking.BookingNumber,
            CustomerEmail: booking.CustomerEmail,
            CustomerName: booking.CustomerName,
            EventName: booking.EventName,
            VenueName: booking.EventVenueName,
            Address: booking.EventAddress,
            EventStartsAt: booking.EventStartsAt,
            TotalAmount: booking.TotalAmount,
            Currency: booking.Currency,
            Tickets: tickets), context.CancellationToken);
    }
}

public sealed class PaymentFailedConsumer(BookingDbContext db, InventorySummaryService inventorySummary, TimeProvider timeProvider, ISeatAvailabilityNotifier availabilityNotifier) : IConsumer<PaymentFailedV1>
{
    public async Task Consume(ConsumeContext<PaymentFailedV1> context)
    {
        var message = context.Message;
        var booking = await db.Bookings.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == message.BookingId, context.CancellationToken);
        if (booking is null || booking.Status != BookingStatus.PendingPayment || booking.UserId != message.UserId) return;
        booking.Cancel();
        var hold = await db.Holds.SingleAsync(x => x.Id == booking.HoldId, context.CancellationToken);
        var inventory = await db.Inventory.Where(x => x.HoldId == booking.HoldId && x.BookingId == booking.Id && x.Status == SeatInventoryStatus.Held)
            .ToListAsync(context.CancellationToken);
        foreach (var seat in inventory) seat.Release(booking.HoldId, booking.Id);
        if (hold.Status == SeatHoldStatus.Converted) hold.ReleaseAfterCancellation();
        await inventorySummary.ApplyDeltaAsync(booking.EventId, inventory.Count, -inventory.Count, 0, context.CancellationToken);
        await db.SaveChangesAsync(context.CancellationToken);
        await availabilityNotifier.NotifyAsync(booking.EventId, inventory.Select(x => x.SeatId).ToArray(), "Available", context.CancellationToken);
        await context.Publish(new BookingCancelledV1(Guid.NewGuid(), message.CorrelationId, timeProvider.GetUtcNow(), 1,
            booking.Id, booking.UserId, message.Reason));
    }
}
