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

namespace FlashSeat.Payment.Infrastructure;

public sealed class PaymentService(PaymentDbContext db, IPublishEndpoint publisher, IHttpClientFactory httpClientFactory,
    IHttpContextAccessor httpContextAccessor, TimeProvider timeProvider) : IPaymentService
{
    public async Task<PaymentResult> CreateAsync(Guid userId, string idempotencyKey, CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
        var existing = await db.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null)
            return existing.UserId == userId && existing.RequestFingerprint == fingerprint
                ? new(ToResponse(existing), false)
                : new(null, true);
        if (await db.Payments.AnyAsync(x => x.BookingId == request.BookingId, cancellationToken)) return new(null, true);

        var bookingClient = httpClientFactory.CreateClient("booking");
        var authorization = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authorization))
            bookingClient.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(authorization);
        using var bookingResponse = await bookingClient.GetAsync($"/internal/bookings/{request.BookingId}", cancellationToken);
        if (!bookingResponse.IsSuccessStatusCode) return new(null, true);
        var booking = await bookingResponse.Content.ReadFromJsonAsync<BookingSnapshot>(cancellationToken)
            ?? throw new InvalidOperationException("Booking response is invalid.");
        if (booking.UserId != userId || booking.Status != "PendingPayment") return new(null, true);

        var now = timeProvider.GetUtcNow();
        var entity = new global::FlashSeat.Payment.Domain.Payment(Guid.NewGuid(), request.BookingId, userId, booking.TotalAmount, booking.Currency, idempotencyKey, fingerprint, now);
        entity.Complete(request.SimulateResult == "Success", now);
        db.Payments.Add(entity);
        var correlationId = Guid.NewGuid();
        if (entity.Status == global::FlashSeat.Payment.Domain.PaymentStatus.Succeeded)
            await publisher.Publish(new PaymentSucceededV1(Guid.NewGuid(), correlationId, now, 1, entity.BookingId, entity.Id, userId, entity.Amount, entity.Currency), cancellationToken);
        else
            await publisher.Publish(new PaymentFailedV1(Guid.NewGuid(), correlationId, now, 1, entity.BookingId, entity.Id, userId, entity.FailureReason!), cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new(ToResponse(entity), false);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var winner = await db.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);
            return winner is not null && winner.UserId == userId && winner.RequestFingerprint == fingerprint
                ? new(ToResponse(winner), false)
                : new(null, true);
        }
    }

    public async Task<PaymentResponse?> GetAsync(Guid userId, bool isAdmin, Guid paymentId, CancellationToken cancellationToken) =>
        await db.Payments.AsNoTracking().Where(x => x.Id == paymentId && (isAdmin || x.UserId == userId))
            .Select(x => new PaymentResponse(x.Id, x.BookingId, x.Amount, x.Currency, x.Status.ToString(), x.FailureReason, x.CreatedAt, x.CompletedAt)).SingleOrDefaultAsync(cancellationToken);
    private static PaymentResponse ToResponse(global::FlashSeat.Payment.Domain.Payment x) => new(x.Id, x.BookingId, x.Amount, x.Currency, x.Status.ToString(), x.FailureReason, x.CreatedAt, x.CompletedAt);
    private sealed record BookingSnapshot(Guid Id, Guid UserId, decimal TotalAmount, string Currency, string Status);
}
