using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlashSeat.Payment.Infrastructure;

public sealed class ExpiredPaymentLinkWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<ExpiredPaymentLinkWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15), timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken)) await CancelExpiredLinksAsync(stoppingToken);
    }

    private async Task CancelExpiredLinksAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var payOsClient = scope.ServiceProvider.GetRequiredService<IPayOSClient>();
        var now = timeProvider.GetUtcNow();
        var payments = await db.Payments
            .Where(x => x.Status == global::FlashSeat.Payment.Domain.PaymentStatus.Pending && x.PaymentLinkExpiresAt <= now && x.PaymentLinkId != null)
            .OrderBy(x => x.PaymentLinkExpiresAt)
            .Take(10)
            .ToListAsync(cancellationToken);

        foreach (var payment in payments)
        {
            try
            {
                await payOsClient.CancelPaymentLinkAsync(payment.PaymentLinkId!, "Payment window expired", cancellationToken);
                payment.MarkExpired(now);
            }
            catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.TooManyRequests)
            {
                logger.LogWarning(exception, "PayOS rate-limited expired payment-link cancellation; retrying on the next worker cycle");
                break;
            }
            catch (HttpRequestException exception)
            {
                logger.LogWarning(exception, "Could not cancel expired PayOS payment link for order {OrderCode}; retrying later", payment.OrderCode);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
