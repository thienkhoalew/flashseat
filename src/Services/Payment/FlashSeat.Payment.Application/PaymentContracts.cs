namespace FlashSeat.Payment.Application;

public sealed record CreatePaymentRequest(Guid BookingId);

public sealed record PayOSPaymentRequest(
    long OrderCode,
    long Amount,
    string Description,
    string CancelUrl,
    string ReturnUrl,
    string Signature,
    long? ExpiredAt = null);

public sealed record PayOSPaymentResponse(
    string? Code,
    string? Desc,
    PayOSPaymentData? Data);

public sealed record PayOSPaymentData(
    string? Bin = null,
    string? AccountNumber = null,
    string? AccountName = null,
    long Amount = 0,
    string? Description = null,
    long OrderCode = 0,
    string? Currency = null,
    string? PaymentLinkId = null,
    string? Status = null,
    string? CheckoutUrl = null,
    string? QrCode = null,
    DateTimeOffset? CreatedAt = null,
    string? ProviderReference = null);

public sealed record PaymentResponse(
    Guid Id,
    Guid BookingId,
    decimal Amount,
    string Currency,
    string Status,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    long OrderCode = 0,
    string? CheckoutUrl = null,
    string? PaymentLinkId = null,
    string? QrCode = null,
    DateTimeOffset? PaymentLinkExpiresAt = null,
    string? ProviderStatus = null,
    string? ProviderReference = null,
    string? BankId = null,
    string? AccountNumber = null,
    string? AccountName = null,
    string? TransferDescription = null);

public sealed record PaymentResult(PaymentResponse? Payment, bool IdempotencyConflict);

public sealed record PayOSWebhookData(
    long OrderCode,
    decimal Amount,
    string Description,
    string? AccountNumber,
    string? Reference,
    string? TransactionDateTime,
    string? Currency,
    string? PaymentLinkId,
    string? Code,
    string? Desc,
    string? CounterAccountBankId = null,
    string? CounterAccountBankName = null,
    string? CounterAccountName = null,
    string? CounterAccountNumber = null,
    string? VirtualAccountName = null,
    string? VirtualAccountNumber = null);

public sealed record PayOSWebhookRequest(
    string Code,
    string Desc,
    bool Success,
    PayOSWebhookData? Data,
    string Signature);

public interface IPaymentService
{
    Task<PaymentResult> CreateAsync(Guid userId, string idempotencyKey, CreatePaymentRequest request, CancellationToken cancellationToken);
    Task<PaymentResponse?> GetAsync(Guid userId, bool isAdmin, Guid paymentId, CancellationToken cancellationToken);
    Task<bool> ProcessPayOSWebhookAsync(PayOSWebhookRequest request, CancellationToken cancellationToken);
}
