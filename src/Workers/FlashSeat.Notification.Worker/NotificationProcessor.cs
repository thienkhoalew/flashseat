using Microsoft.Extensions.Options;

namespace FlashSeat.Notification.Worker;

public sealed partial class NotificationProcessor(
    NotificationBuffer queue,
    IOptions<NotificationWorkerOptions> options,
    ILogger<NotificationProcessor> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, options.Value.ConsumerCount)
            .Select(index => ProcessAsync(index, stoppingToken)));

    private async Task ProcessAsync(int consumerId, CancellationToken cancellationToken)
    {
        await foreach (var command in queue.ReadAllAsync(cancellationToken))
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await Task.Delay(30, cancellationToken);
                    NotificationSent(logger, consumerId, command.MessageId, command.Subject);
                    break;
                }
                catch (Exception exception) when (
                    exception is not OperationCanceledException && attempt <= options.Value.MaxRetryCount)
                {
                    NotificationRetry(logger, exception, attempt, command.MessageId);
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken);
                }
            }
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Simulated email sent by consumer {ConsumerId}: {MessageId} {Subject}")]
    private static partial void NotificationSent(ILogger logger, int consumerId, Guid messageId, string subject);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Notification retry {Attempt} for {MessageId}")]
    private static partial void NotificationRetry(ILogger logger, Exception exception, int attempt, Guid messageId);
}
