using System.Text;
using FlashSeat.Contracts;
using MassTransit;

namespace FlashSeat.Notification.Worker;

public sealed class BookingConfirmedConsumer(NotificationBuffer queue) : IConsumer<BookingConfirmedV1>
{
    public Task Consume(ConsumeContext<BookingConfirmedV1> context)
    {
        var msg = context.Message;
        var subject = $"[FlashSeat] Xác nhận vé: {msg.EventName} - Đơn hàng #{msg.BookingNumber}";

        var htmlBuilder = new StringBuilder();
        htmlBuilder.Append($"""
<div style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 24px; border: 1px solid #e2e8f0; border-radius: 12px; background: #ffffff;">
    <div style="border-bottom: 2px solid #2563eb; padding-bottom: 16px; margin-bottom: 20px;">
        <span style="font-size: 12px; font-weight: 700; letter-spacing: 2px; color: #2563eb; text-transform: uppercase;">FlashSeat Box Office</span>
        <h1 style="color: #0f172a; margin: 8px 0 0; font-size: 22px;">Xác nhận đặt vé thành công!</h1>
        <p style="color: #64748b; margin: 4px 0 0; font-size: 14px;">Mã đơn: <strong style="color: #0f172a; font-family: monospace;">{msg.BookingNumber}</strong></p>
    </div>

    <p style="font-size: 15px; color: #334155; line-height: 1.5; margin: 0 0 16px;">
        Xin chào <strong>{(string.IsNullOrWhiteSpace(msg.CustomerName) ? "Quý khách" : msg.CustomerName)}</strong>,
    </p>
    <p style="font-size: 15px; color: #334155; line-height: 1.5; margin: 0 0 20px;">
        Bạn đã thanh toán thành công và nhận được vé tham dự sự kiện tại <strong>FlashSeat</strong>.
    </p>

    <div style="background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 16px; margin-bottom: 24px;">
        <h2 style="font-size: 17px; color: #0f172a; margin: 0 0 8px;">{msg.EventName}</h2>
        <p style="margin: 4px 0; font-size: 14px; color: #475569;">📍 <strong>Địa điểm:</strong> {msg.VenueName}{(string.IsNullOrWhiteSpace(msg.Address) ? "" : $" - {msg.Address}")}</p>
        <p style="margin: 4px 0; font-size: 14px; color: #475569;">⏰ <strong>Thời gian:</strong> {(msg.EventStartsAt.HasValue ? msg.EventStartsAt.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : "Xem chi tiết sự kiện")}</p>
        <p style="margin: 4px 0; font-size: 14px; color: #475569;">💰 <strong>Tổng thanh toán:</strong> {msg.TotalAmount:N0} {msg.Currency}</p>
    </div>

    <h3 style="font-size: 15px; color: #0f172a; margin: 0 0 12px; letter-spacing: 0.5px;">DANH SÁCH VÉ:</h3>
    <table style="width: 100%; border-collapse: collapse; margin-bottom: 24px; font-size: 14px;">
        <thead>
            <tr style="background: #f1f5f9; text-align: left;">
                <th style="padding: 8px 12px; border: 1px solid #e2e8f0;">Vị trí ghế</th>
                <th style="padding: 8px 12px; border: 1px solid #e2e8f0;">Giá vé</th>
                <th style="padding: 8px 12px; border: 1px solid #e2e8f0;">Mã vé Check-in</th>
            </tr>
        </thead>
        <tbody>
""");

        if (msg.Tickets is not null && msg.Tickets.Count > 0)
        {
            foreach (var ticket in msg.Tickets)
            {
                htmlBuilder.Append($"""
            <tr>
                <td style="padding: 8px 12px; border: 1px solid #e2e8f0; font-weight: 600;">{ticket.Section} · {ticket.Row}{ticket.Number}</td>
                <td style="padding: 8px 12px; border: 1px solid #e2e8f0;">{ticket.Price:N0} {ticket.Currency}</td>
                <td style="padding: 8px 12px; border: 1px solid #e2e8f0; font-family: monospace; font-size: 15px; font-weight: 700; color: #2563eb;">{ticket.TicketCode}</td>
            </tr>
""");
            }
        }
        else
        {
            htmlBuilder.Append("""
            <tr><td colspan="3" style="padding: 12px; text-align: center; color: #64748b;">Chi tiết vé trong tài khoản của bạn.</td></tr>
""");
        }

        htmlBuilder.Append("""
        </tbody>
    </table>

    <div style="text-align: center; margin: 28px 0 16px;">
        <a href="https://flashseat.thienkhoa.name.vn/bookings" style="background: #2563eb; color: #ffffff; padding: 12px 28px; text-decoration: none; border-radius: 6px; font-weight: 600; font-size: 15px; display: inline-block;">Xem vé trên FlashSeat</a>
    </div>

    <p style="font-size: 12px; color: #94a3b8; text-align: center; margin-top: 24px; line-height: 1.4;">
        Vui lòng xuất trình mã vé tại quầy soát vé của sự kiện.<br/>
        Email này được gửi tự động từ hệ thống FlashSeat.
    </p>
</div>
""");

        return queue.WriteAsync(new NotificationCommand(
            msg.MessageId,
            msg.UserId,
            subject,
            htmlBuilder.ToString(),
            ToEmail: string.IsNullOrWhiteSpace(msg.CustomerEmail) ? null : msg.CustomerEmail,
            ToName: msg.CustomerName,
            IsHtml: true), context.CancellationToken).AsTask();
    }
}

public sealed class BookingCancelledConsumer(NotificationBuffer queue) : IConsumer<BookingCancelledV1>
{
    public Task Consume(ConsumeContext<BookingCancelledV1> context) => queue.WriteAsync(
        new NotificationCommand(
            context.Message.MessageId,
            context.Message.UserId,
            "Booking cancelled",
            context.Message.Reason), context.CancellationToken).AsTask();
}
