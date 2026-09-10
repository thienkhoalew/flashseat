using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlashSeat.Contracts;
using FlashSeat.Payment.Application;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlashSeat.Payment.Infrastructure;

public sealed class PaymentService(
    PaymentDbContext db,
    IPublishEndpoint publisher,
    IHttpClientFactory httpClientFactory,
    IHttpContextAccessor httpContextAccessor,
    IPayOSClient payOsClient,
    IOptions<PayOSOptions> payOsOptions,
    TimeProvider timeProvider,
    ILogger<PaymentService> logger) : IPaymentService
{
    private readonly PayOSOptions payOs = payOsOptions.Value;

    public async Task<PaymentResult> CreateAsync(Guid userId, string idempotencyKey, CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
        var existing = await db.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null)
            return existing.UserId == userId && existing.RequestFingerprint == fingerprint
                ? new(ToResponse(existing), false)
                : new(null, true);

        var existingByBooking = await db.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.BookingId == request.BookingId, cancellationToken);
        if (existingByBooking is not null)
        {
            if (existingByBooking.UserId != userId) return new(null, true);
            return new(ToResponse(existingByBooking), false);
        }

        var bookingClient = httpClientFactory.CreateClient("booking");
        var authorization = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authorization))
            bookingClient.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(authorization);
        using var bookingResponse = await bookingClient.GetAsync($"/internal/bookings/{request.BookingId}", cancellationToken);
        if (!bookingResponse.IsSuccessStatusCode) return new(null, true);
        var booking = await bookingResponse.Content.ReadFromJsonAsync<BookingSnapshot>(cancellationToken)
            ?? throw new InvalidOperationException("Booking response is invalid.");
        var now = timeProvider.GetUtcNow();
        if (booking.UserId != userId || booking.Status != "PendingPayment" || booking.PaymentDueAt <= now) return new(null, true);
        if (!string.Equals(booking.Currency, "VND", StringComparison.OrdinalIgnoreCase) || booking.TotalAmount <= 0 || booking.TotalAmount != decimal.Truncate(booking.TotalAmount) || booking.TotalAmount > int.MaxValue)
            return new(null, true);

        var entity = new global::FlashSeat.Payment.Domain.Payment(Guid.NewGuid(), request.BookingId, userId, booking.TotalAmount, booking.Currency, idempotencyKey, fingerprint, now);
        entity.SetOrderCode((long)RandomNumberGenerator.GetInt32(100000, int.MaxValue));
        db.Payments.Add(entity);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var winner = await db.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);
            return winner is not null && winner.UserId == userId && winner.RequestFingerprint == fingerprint
                ? new(ToResponse(winner), false)
                : new(null, true);
        }

        var description = $"FS {entity.OrderCode}";
        var returnUrl = ResolveUrl(payOs.ReturnUrl, booking.HoldId, entity.OrderCode);
        var cancelUrl = ResolveUrl(payOs.CancelUrl, booking.HoldId, entity.OrderCode);
        if (string.IsNullOrWhiteSpace(payOs.WebhookUrl)) throw new InvalidOperationException("PayOS webhook URL is required.");
        var requestWithoutSignature = new PayOSPaymentRequest(entity.OrderCode, Convert.ToInt64(entity.Amount), description, cancelUrl, returnUrl, "", booking.PaymentDueAt.ToUnixTimeSeconds());
        var signedRequest = requestWithoutSignature with { Signature = PayOSClient.CreatePaymentSignature(requestWithoutSignature, payOs.ChecksumKey) };
        try
        {
            var link = await payOsClient.CreatePaymentLinkAsync(signedRequest, cancellationToken);
            entity.SetPayOSLink(link.PaymentLinkId, link.CheckoutUrl, link.QrCode, booking.PaymentDueAt, link.Status);
            await db.SaveChangesAsync(cancellationToken);
            return new(ToResponse(entity), false);
        }
        catch (HttpRequestException)
        {
            entity.SetFailureReason("PayOS payment link could not be created.");
            await db.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task<PaymentResponse?> GetAsync(Guid userId, bool isAdmin, Guid paymentId, CancellationToken cancellationToken)
    {
        var entity = await db.Payments.AsNoTracking()
            .Where(x => x.Id == paymentId && (isAdmin || x.UserId == userId))
            .SingleOrDefaultAsync(cancellationToken);
        return entity is null ? null : ToResponse(entity);
    }

    public async Task<bool> ProcessPayOSWebhookAsync(PayOSWebhookRequest request, CancellationToken cancellationToken)
    {
        if (request is null || request.Data is null || !string.Equals(request.Code, "00", StringComparison.Ordinal) || !request.Success || !string.Equals(request.Data.Code, "00", StringComparison.Ordinal))
        {
            logger.LogWarning("Rejected PayOS webhook envelope: outerCode={OuterCode}, success={Success}, hasData={HasData}", request?.Code, request?.Success, request?.Data is not null);
            return false;
        }
        if (string.IsNullOrWhiteSpace(payOs.ChecksumKey) || string.IsNullOrWhiteSpace(request.Signature) || !VerifyPayOSSignature(request.Data, request.Signature, payOs.ChecksumKey))
        {
            logger.LogWarning("Rejected PayOS webhook signature for order {OrderCode}", request.Data.OrderCode);
            return false;
        }

        var data = request.Data;
        var entity = await db.Payments.SingleOrDefaultAsync(x => x.OrderCode == data.OrderCode, cancellationToken);
        if (entity is null)
        {
            logger.LogInformation("Acknowledged PayOS webhook for unknown order {OrderCode}", data.OrderCode);
            return true;
        }
        var now = timeProvider.GetUtcNow();
        var paymentWindowExpired = entity.PaymentLinkExpiresAt is { } paymentLinkExpiresAt && paymentLinkExpiresAt <= now;
        if (data.Amount != entity.Amount || (!string.IsNullOrWhiteSpace(data.Currency) && !string.Equals(data.Currency, entity.Currency, StringComparison.OrdinalIgnoreCase)))
        {
            logger.LogWarning("Rejected PayOS webhook amount or currency for order {OrderCode}", data.OrderCode);
            return false;
        }
        if (!string.IsNullOrWhiteSpace(data.PaymentLinkId) && !string.Equals(data.PaymentLinkId, entity.PaymentLinkId, StringComparison.Ordinal))
        {
            logger.LogWarning("Rejected PayOS webhook payment link for order {OrderCode}", data.OrderCode);
            return false;
        }
        if (entity.Status != global::FlashSeat.Payment.Domain.PaymentStatus.Pending || paymentWindowExpired)
        {
            if (paymentWindowExpired && entity.RecordLatePayment(now, data.Reference))
            {
                await db.SaveChangesAsync(cancellationToken);
                logger.LogWarning("Received late PayOS payment for order {OrderCode}; booking will not be confirmed", data.OrderCode);
            }
            return true;
        }
        logger.LogInformation("Processing PayOS webhook for order {OrderCode}", data.OrderCode);

        var eventKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{data.OrderCode}|{data.PaymentLinkId}|{data.Reference}|{data.TransactionDateTime}|{data.Amount}|{data.Code}")));
        if (await db.PaymentWebhookReceipts.AnyAsync(x => x.EventKey == eventKey, cancellationToken)) return true;
        db.PaymentWebhookReceipts.Add(new PaymentWebhookReceipt { EventKey = eventKey, PaymentId = entity.Id, OrderCode = data.OrderCode, ReceivedAt = now });
        if (!entity.TryComplete(true, now)) return true;
        if (!string.IsNullOrWhiteSpace(data.Reference)) entity.SetProviderReference(data.Reference);
        if (!string.IsNullOrWhiteSpace(data.Code)) entity.SetProviderStatus(data.Code);
        await publisher.Publish(new PaymentSucceededV1(Guid.NewGuid(), Guid.NewGuid(), now, 1, entity.BookingId, entity.Id, entity.UserId, entity.Amount, entity.Currency), cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return await db.PaymentWebhookReceipts.AnyAsync(x => x.EventKey == eventKey, cancellationToken);
        }
        return true;
    }

    private static bool VerifyPayOSSignature(PayOSWebhookData data, string signature, string checksumKey)
    {
        var canonical = string.Join('&', new SortedDictionary<string, string?>
        {
            ["accountNumber"] = data.AccountNumber,
            ["amount"] = data.Amount.ToString("0", System.Globalization.CultureInfo.InvariantCulture),
            ["code"] = data.Code,
            ["counterAccountBankId"] = data.CounterAccountBankId,
            ["counterAccountBankName"] = data.CounterAccountBankName,
            ["counterAccountName"] = data.CounterAccountName,
            ["counterAccountNumber"] = data.CounterAccountNumber,
            ["currency"] = data.Currency,
            ["desc"] = data.Desc,
            ["description"] = data.Description,
            ["orderCode"] = data.OrderCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["paymentLinkId"] = data.PaymentLinkId,
            ["reference"] = data.Reference,
            ["transactionDateTime"] = data.TransactionDateTime,
            ["virtualAccountName"] = data.VirtualAccountName,
            ["virtualAccountNumber"] = data.VirtualAccountNumber
        }.Select(x => $"{x.Key}={(x.Value is null or "null" or "undefined" ? "" : x.Value)}"));
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(checksumKey));
        var expected = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical));
        byte[] actual;
        try { actual = Convert.FromHexString(signature); }
        catch (FormatException) { return false; }
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static string ResolveUrl(string template, Guid holdId, long orderCode)
    {
        if (string.IsNullOrWhiteSpace(template)) throw new InvalidOperationException("PayOS return and cancel URLs are required.");
        return template.Replace("{holdId}", holdId.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{orderCode}", orderCode.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private PaymentResponse ToResponse(global::FlashSeat.Payment.Domain.Payment x) => new(
        x.Id, x.BookingId, x.Amount, x.Currency, x.Status.ToString(), x.FailureReason, x.CreatedAt, x.CompletedAt,
        x.OrderCode, x.CheckoutUrl, x.PaymentLinkId, x.QrCode, x.PaymentLinkExpiresAt, x.ProviderStatus, x.ProviderReference,
        payOs.BankId, payOs.AccountNumber, payOs.AccountName, $"FS {x.OrderCode}");

    private sealed record BookingSnapshot(Guid Id, Guid UserId, Guid HoldId, decimal TotalAmount, string Currency, string Status, DateTimeOffset PaymentDueAt);
}
