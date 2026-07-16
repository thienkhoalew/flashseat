namespace FlashSeat.Payment.Application;

public sealed record CreatePaymentRequest(Guid BookingId, string SimulateResult);
public sealed record PaymentResponse(Guid Id, Guid BookingId, decimal Amount, string Currency, string Status, string? FailureReason, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);
public sealed record PaymentResult(PaymentResponse? Payment, bool IdempotencyConflict);
public interface IPaymentService
{
    Task<PaymentResult> CreateAsync(Guid userId, string idempotencyKey, CreatePaymentRequest request, CancellationToken cancellationToken);
    Task<PaymentResponse?> GetAsync(Guid userId, bool isAdmin, Guid paymentId, CancellationToken cancellationToken);
}
