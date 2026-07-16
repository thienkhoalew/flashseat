using System.ComponentModel.DataAnnotations;
namespace FlashSeat.Notification.Worker;
public sealed class NotificationWorkerOptions
{
    public const string SectionName = "NotificationWorker";
    [Range(10, 5000)] public int ChannelCapacity { get; init; } = 500;
    [Range(1, 32)] public int ConsumerCount { get; init; } = 4;
    [Range(0, 10)] public int MaxRetryCount { get; init; } = 3;
}
