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
    public long OrderCode { get; private set; }
    public string? PaymentLinkId { get; private set; }
    public string? CheckoutUrl { get; private set; }
    public string? QrCode { get; private set; }
    public DateTimeOffset? PaymentLinkExpiresAt { get; private set; }
    public string? ProviderReference { get; private set; }
    public string? ProviderStatus { get; private set; }
    public void SetOrderCode(long orderCode) => OrderCode = orderCode;
    public void SetPayOSLink(string paymentLinkId, string checkoutUrl, string? qrCode, DateTimeOffset? expiresAt, string? providerStatus = null)
    { PaymentLinkId = paymentLinkId; CheckoutUrl = checkoutUrl; QrCode = qrCode; PaymentLinkExpiresAt = expiresAt; ProviderStatus = providerStatus; FailureReason = null; }
    public void SetFailureReason(string reason) => FailureReason = reason;
    public void MarkExpired(DateTimeOffset now)
    {
        if (Status != PaymentStatus.Pending) return;
        Status = PaymentStatus.Failed;
        FailureReason = "Payment link expired before payment was completed.";
        ProviderStatus = "EXPIRED";
        CompletedAt = now;
    }
    public void SetProviderReference(string reference) => ProviderReference = reference;
    public void SetProviderStatus(string status) => ProviderStatus = status;
    public bool RecordLatePayment(DateTimeOffset now, string? providerReference)
    {
        if (Status == PaymentStatus.Succeeded || ProviderStatus == "LATE_PAYMENT") return false;
        if (Status == PaymentStatus.Pending) MarkExpired(now);
        if (Status != PaymentStatus.Failed || ProviderStatus != "EXPIRED") return false;
        FailureReason = "Payment arrived after the payment window expired; manual refund review is required.";
        ProviderStatus = "LATE_PAYMENT";
        if (!string.IsNullOrWhiteSpace(providerReference)) ProviderReference = providerReference;
        return true;
    }
    public bool TryComplete(bool success, DateTimeOffset now, string? failureReason = null)
    {
        if (Status != PaymentStatus.Pending) return false;
        Status = success ? PaymentStatus.Succeeded : PaymentStatus.Failed;
        FailureReason = success ? null : failureReason ?? "Payment failed";
        CompletedAt = now;
        return true;
    }
}
