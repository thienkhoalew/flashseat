namespace FlashSeat.Payment.Infrastructure;

public sealed class PaymentWebhookReceipt
{
    public long Id { get; set; }
    public string EventKey { get; set; } = string.Empty;
    public Guid PaymentId { get; set; }
    public long OrderCode { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}
