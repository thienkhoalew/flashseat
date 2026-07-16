using FlashSeat.Contracts;
using MassTransit;
namespace FlashSeat.Notification.Worker;
public sealed class BookingConfirmedConsumer(NotificationBuffer queue) : IConsumer<BookingConfirmedV1>
{
    public Task Consume(ConsumeContext<BookingConfirmedV1> context) => queue.WriteAsync(new NotificationCommand(context.Message.MessageId, context.Message.UserId, $"Vé {context.Message.BookingNumber} đã xác nhận", "Cảm ơn bạn đã đặt vé tại FlashSeat."), context.CancellationToken).AsTask();
}
public sealed class BookingCancelledConsumer(NotificationBuffer queue) : IConsumer<BookingCancelledV1>
{
    public Task Consume(ConsumeContext<BookingCancelledV1> context) => queue.WriteAsync(new NotificationCommand(context.Message.MessageId, context.Message.UserId, "Đặt vé đã hủy", context.Message.Reason), context.CancellationToken).AsTask();
}
