using System.Net;
using System.Net.Http.Json;
using FlashSeat.Payment.Application;
using FlashSeat.Payment.Domain;
using FlashSeat.Payment.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Options;
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

    [Fact]
    public async Task PayOS_client_accepts_a_complete_payment_link_response()
    {
        var client = CreatePayOSClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                code = "00",
                desc = "success",
                data = PaymentLink(123456, 2000)
            })
        });

        var result = await client.CreatePaymentLinkAsync(
            new PayOSPaymentRequest(123456, 2000, "FS 123456", "https://cancel.test", "https://return.test", "signature"),
            CancellationToken.None);

        result.PaymentLinkId.Should().Be("link-id");
        result.QrCode.Should().Be("qr-data");
        result.Amount.Should().Be(2000);
    }

    [Fact]
    public async Task PayOS_client_rejects_a_link_without_qr_data()
    {
        var client = CreatePayOSClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                code = "00",
                desc = "success",
                data = new
                {
                    paymentLinkId = "link-id",
                    checkoutUrl = "https://checkout.test",
                    amount = 2000,
                    orderCode = 123456,
                    currency = "VND"
                }
            })
        });

        var action = () => client.CreatePaymentLinkAsync(
            new PayOSPaymentRequest(123456, 2000, "FS 123456", "https://cancel.test", "https://return.test", "signature"),
            CancellationToken.None);

        await action.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*order 123456*qrCode*");
    }

    [Fact]
    public async Task PayOS_client_returns_null_when_payment_link_lookup_is_not_found()
    {
        var client = CreatePayOSClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await client.GetPaymentLinkAsync(123456, 2000, CancellationToken.None);

        result.Should().BeNull();
    }

    private static PayOSClient CreatePayOSClient(
        Func<HttpRequestMessage, HttpResponseMessage> response)
    {
        var httpClient = new HttpClient(new TestHandler(response))
        {
            BaseAddress = new Uri("https://api-merchant.payos.vn/")
        };
        return new PayOSClient(
            httpClient,
            Options.Create(new PayOSOptions
            {
                ClientId = "client-id",
                ApiKey = "api-key",
                ChecksumKey = "checksum-key"
            }));
    }

    private static object PaymentLink(long orderCode, long amount) => new
    {
        bin = "970422",
        accountNumber = "123456789",
        accountName = "FLASHSEAT",
        amount,
        description = $"FS {orderCode}",
        orderCode,
        currency = "VND",
        paymentLinkId = "link-id",
        status = "PENDING",
        checkoutUrl = "https://checkout.test",
        qrCode = "qr-data"
    };

    private sealed class TestHandler(
        Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }
}
