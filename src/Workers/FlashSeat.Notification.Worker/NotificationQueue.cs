using System.Threading.Channels;
using Microsoft.Extensions.Options;
namespace FlashSeat.Notification.Worker;
public sealed record NotificationCommand(Guid MessageId, Guid UserId, string Subject, string Body);
public sealed class NotificationBuffer
{
    private readonly Channel<NotificationCommand> _channel;
    public NotificationBuffer(IOptions<NotificationWorkerOptions> options) => _channel = Channel.CreateBounded<NotificationCommand>(new BoundedChannelOptions(options.Value.ChannelCapacity) { FullMode = BoundedChannelFullMode.Wait, SingleWriter = false, SingleReader = false });
    public ValueTask WriteAsync(NotificationCommand command, CancellationToken ct) => _channel.Writer.WriteAsync(command, ct);
    public IAsyncEnumerable<NotificationCommand> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}
