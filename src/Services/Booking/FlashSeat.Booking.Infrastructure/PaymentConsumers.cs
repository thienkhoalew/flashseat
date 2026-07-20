using FlashSeat.Booking.Domain;
using FlashSeat.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace FlashSeat.Booking.Infrastructure;

public sealed class PaymentSucceededConsumer(BookingDbContext db, TimeProvider timeProvider) : IConsumer<PaymentSucceededV1>
{
    public async Task Consume(ConsumeContext<PaymentSucceededV1> context)
    {
        var message = context.Message;
        var booking = await db.Bookings.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == message.BookingId, context.CancellationToken);
        if (booking is null || booking.Status != BookingStatus.PendingPayment || booking.UserId != message.UserId ||
            booking.TotalAmount != message.Amount || booking.Currency != message.Currency) return;
        var hold = await db.Holds.SingleAsync(x => x.Id == booking.HoldId, context.CancellationToken);
        if (hold.ExpiresAt < message.OccurredAt) return;
        var inventory = await db.Inventory.Where(x => x.HoldId == hold.Id && x.BookingId == booking.Id && x.Status == SeatInventoryStatus.Held)
            .ToListAsync(context.CancellationToken);
        if (inventory.Count != booking.Items.Count) return;
        booking.Confirm(message.PaymentId, timeProvider.GetUtcNow());
        foreach (var seat in inventory) seat.Book(booking.Id);
        await db.SaveChangesAsync(context.CancellationToken);
        await context.Publish(new BookingConfirmedV1(Guid.NewGuid(), message.CorrelationId, timeProvider.GetUtcNow(), 1,
            booking.Id, booking.UserId, booking.BookingNumber));
    }
}

public sealed class PaymentFailedConsumer(BookingDbContext db, TimeProvider timeProvider) : IConsumer<PaymentFailedV1>
{
    public async Task Consume(ConsumeContext<PaymentFailedV1> context)
    {
        var message = context.Message;
        var booking = await db.Bookings.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == message.BookingId, context.CancellationToken);
        if (booking is null || booking.Status != BookingStatus.PendingPayment || booking.UserId != message.UserId) return;
        booking.Cancel();
        var inventory = await db.Inventory.Where(x => x.HoldId == booking.HoldId && x.BookingId == booking.Id && x.Status == SeatInventoryStatus.Held)
            .ToListAsync(context.CancellationToken);
        foreach (var seat in inventory) seat.Release(booking.HoldId, booking.Id);
        await db.SaveChangesAsync(context.CancellationToken);
        await context.Publish(new BookingCancelledV1(Guid.NewGuid(), message.CorrelationId, timeProvider.GetUtcNow(), 1,
            booking.Id, booking.UserId, message.Reason));
    }
}
