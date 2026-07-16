namespace FlashSeat.Payment.Domain;

public enum PaymentStatus { Pending, Succeeded, Failed }

public sealed class Payment
{
    private Payment() { }
    public Payment(Guid id, Guid bookingId, Guid userId, decimal amount, string currency, string key, string fingerprint, DateTimeOffset createdAt)
    { Id = id; BookingId = bookingId; UserId = userId; Amount = amount; Currency = currency; IdempotencyKey = key; RequestFingerprint = fingerprint; CreatedAt = createdAt; }
    public Guid Id { get; private set; }
    public Guid BookingId { get; private set; }
    public Guid UserId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "VND";
    public PaymentStatus Status { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestFingerprint { get; private set; } = string.Empty;
    public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public void Complete(bool success, DateTimeOffset now) { Status = success ? PaymentStatus.Succeeded : PaymentStatus.Failed; FailureReason = success ? null : "Simulated payment failure"; CompletedAt = now; }
}
