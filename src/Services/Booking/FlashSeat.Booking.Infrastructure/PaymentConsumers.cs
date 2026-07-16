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
        if (booking is null || booking.Status == BookingStatus.Confirmed) return;
        var hold = await db.Holds.SingleAsync(x => x.Id == booking.HoldId, context.CancellationToken);
        if (hold.ExpiresAt <= timeProvider.GetUtcNow()) return;
        booking.Confirm(message.PaymentId, timeProvider.GetUtcNow());
        var seatIds = booking.Items.Select(x => x.SeatId).ToArray();
        var inventory = await db.Inventory.Where(x => seatIds.Contains(x.SeatId)).ToListAsync(context.CancellationToken);
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
        if (booking is null || booking.Status != BookingStatus.PendingPayment) return;
        booking.Cancel();
        var seatIds = booking.Items.Select(x => x.SeatId).ToArray();
        var inventory = await db.Inventory.Where(x => seatIds.Contains(x.SeatId)).ToListAsync(context.CancellationToken);
        foreach (var seat in inventory) seat.Release();
        await db.SaveChangesAsync(context.CancellationToken);
        await context.Publish(new BookingCancelledV1(Guid.NewGuid(), message.CorrelationId, timeProvider.GetUtcNow(), 1,
            booking.Id, booking.UserId, message.Reason));
    }
}
