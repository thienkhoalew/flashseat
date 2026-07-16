using StackExchange.Redis;

namespace FlashSeat.Booking.Infrastructure;

public sealed class RedisSeatLock(IConnectionMultiplexer redis)
{
    private const string ReleaseScript = "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

    public async Task<IAsyncDisposable?> AcquireAsync(Guid eventId, IEnumerable<Guid> seatIds)
    {
        var database = redis.GetDatabase();
        var token = Guid.NewGuid().ToString("N");
        var keys = seatIds.Order().Select(x => (RedisKey)$"lock:seat:{eventId:N}:{x:N}").ToArray();
        var acquired = new List<RedisKey>();
        foreach (var key in keys)
        {
            if (!await database.StringSetAsync(key, token, TimeSpan.FromSeconds(10), When.NotExists))
            {
                await ReleaseAsync(database, acquired, token);
                return null;
            }
            acquired.Add(key);
        }
        return new Lease(database, acquired, token);
    }

    private static async Task ReleaseAsync(IDatabase database, IEnumerable<RedisKey> keys, string token)
    {
        foreach (var key in keys) await database.ScriptEvaluateAsync(ReleaseScript, [key], [(RedisValue)token]);
    }

    private sealed class Lease(IDatabase database, IReadOnlyCollection<RedisKey> keys, string token) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await ReleaseAsync(database, keys, token);
    }
}
