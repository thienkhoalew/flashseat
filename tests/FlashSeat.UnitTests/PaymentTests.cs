using FlashSeat.Payment.Domain;
using FluentAssertions;
using Xunit;

namespace FlashSeat.UnitTests;

public sealed class PaymentTests
{
    [Fact]
    public void Expired_payment_is_failed_and_cannot_be_completed()
    {
        var createdAt = new DateTimeOffset(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);
        var completedAt = createdAt.AddMinutes(5);
        var payment = new global::FlashSeat.Payment.Domain.Payment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2000, "VND", "idempotency-key", "fingerprint", createdAt);
        payment.SetOrderCode(123456);
        payment.SetPayOSLink("link-id", "https://example.test", "qr", completedAt);

        payment.MarkExpired(completedAt);

        payment.Status.Should().Be(PaymentStatus.Failed);
        payment.FailureReason.Should().Be("Payment link expired before payment was completed.");
        payment.ProviderStatus.Should().Be("EXPIRED");
        payment.CompletedAt.Should().Be(completedAt);
        payment.TryComplete(true, completedAt.AddSeconds(1)).Should().BeFalse();
        payment.Status.Should().Be(PaymentStatus.Failed);
    }

    [Fact]
    public void Late_payment_can_be_recorded_for_manual_refund_review()
    {
        var createdAt = new DateTimeOffset(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);
        var expiredAt = createdAt.AddMinutes(5);
        var payment = new global::FlashSeat.Payment.Domain.Payment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3000, "VND", "idempotency-key", "fingerprint", createdAt);

        payment.MarkExpired(expiredAt);
        payment.RecordLatePayment(expiredAt, "provider-reference").Should().BeTrue();

        payment.Status.Should().Be(PaymentStatus.Failed);
        payment.FailureReason.Should().Contain("manual refund review");
        payment.ProviderStatus.Should().Be("LATE_PAYMENT");
        payment.ProviderReference.Should().Be("provider-reference");
        payment.CompletedAt.Should().Be(expiredAt);
    }

    [Fact]
    public void Pending_payment_can_be_recorded_as_late_after_expiry_worker_marks_it_expired()
    {
        var createdAt = new DateTimeOffset(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);
        var payment = new global::FlashSeat.Payment.Domain.Payment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5000, "VND", "idempotency-key", "fingerprint", createdAt);

        payment.MarkExpired(createdAt.AddMinutes(5));

        payment.RecordLatePayment(createdAt.AddMinutes(6), "provider-reference").Should().BeTrue();
        payment.ProviderStatus.Should().Be("LATE_PAYMENT");
        payment.ProviderReference.Should().Be("provider-reference");
        payment.FailureReason.Should().Contain("manual refund review");
    }

    [Fact]
    public void Expiry_does_not_change_a_confirmed_payment()
    {
        var now = DateTimeOffset.UtcNow;
        var payment = new global::FlashSeat.Payment.Domain.Payment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 4000, "VND", "idempotency-key", "fingerprint", now);

        payment.TryComplete(true, now.AddMinutes(1)).Should().BeTrue();
        payment.MarkExpired(now.AddMinutes(6));

        payment.Status.Should().Be(PaymentStatus.Succeeded);
        payment.FailureReason.Should().BeNull();
        payment.CompletedAt.Should().Be(now.AddMinutes(1));
    }
}
