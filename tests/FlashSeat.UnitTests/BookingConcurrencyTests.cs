using FlashSeat.Booking.Application;
using FlashSeat.Booking.Domain;
using FlashSeat.Booking.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text;
using System.Text.Json;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace FlashSeat.UnitTests;

public sealed class BookingConcurrencyTests
{
    [Fact]
    public async Task Concurrent_holds_for_same_seat_have_exactly_one_winner()
    {
        await using var postgres = new PostgreSqlBuilder().WithDatabase("flashseat_booking_tests").Build();
        await using var redis = new RedisBuilder().Build();
        await Task.WhenAll(postgres.StartAsync(), redis.StartAsync());

        var options = new DbContextOptionsBuilder<BookingDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        var eventId = Guid.NewGuid();
        var seatId = Guid.NewGuid();
        await using (var setup = new BookingDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Inventory.Add(new EventSeatInventory(Guid.NewGuid(), eventId, seatId, "Main", "A", 1, 100, "VND"));
            await setup.SaveChangesAsync();
        }

        await using var redisA = await ConnectionMultiplexer.ConnectAsync(RedisOptions(redis.GetConnectionString(), 0));
        await using var redisB = await ConnectionMultiplexer.ConnectAsync(RedisOptions(redis.GetConnectionString(), 1));
        await using var dbA = new BookingDbContext(options);
        await using var dbB = new BookingDbContext(options);
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var serviceA = new BookingService(dbA, new RedisSeatLock(redisA), OpenSalesClient(), TimeProvider.System);
        var serviceB = new BookingService(dbB, new RedisSeatLock(redisB), OpenSalesClient(), TimeProvider.System);
        var request = new CreateHoldRequest(eventId, [seatId]);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<HoldAttemptResult> Attempt(BookingService service, Guid userId)
        {
            await start.Task;
            return await service.CreateHoldAsync(userId, request, CancellationToken.None);
        }

        var attempts = new[] { Attempt(serviceA, userA), Attempt(serviceB, userB) };
        start.SetResult();
        var results = await Task.WhenAll(attempts);

        results.Count(x => x.Hold is not null).Should().Be(1);
        results.Count(x => x.Hold is null && x.UnavailableSeatIds.SequenceEqual([seatId])).Should().Be(1);

        await using var verify = new BookingDbContext(options);
        var activeHolds = await verify.Holds.Where(x => x.Status == SeatHoldStatus.Active).ToListAsync();
        var inventory = await verify.Inventory.SingleAsync(x => x.EventId == eventId && x.SeatId == seatId);
        activeHolds.Should().ContainSingle();
        inventory.Status.Should().Be(SeatInventoryStatus.Held);
        inventory.HoldId.Should().Be(activeHolds[0].Id);
        results.Single(x => x.Hold is null).Hold.Should().BeNull("the losing request must not receive a hold ID for booking or payment");
    }

    [Fact]
    public async Task Partial_overlap_returns_only_conflicting_seats()
    {
        await using var postgres = new PostgreSqlBuilder().WithDatabase("flashseat_booking_overlap_tests").Build();
        await using var redis = new RedisBuilder().Build();
        await Task.WhenAll(postgres.StartAsync(), redis.StartAsync());

        var options = new DbContextOptionsBuilder<BookingDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        var eventId = Guid.NewGuid();
        var seats = Enumerable.Range(4, 4).ToDictionary(number => number, _ => Guid.NewGuid());
        await using (var setup = new BookingDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Inventory.AddRange(seats.Select(pair => new EventSeatInventory(Guid.NewGuid(), eventId, pair.Value, "Main", "A", pair.Key, pair.Key * 10, "VND")));
            await setup.SaveChangesAsync();
        }

        await using var connection = await ConnectionMultiplexer.ConnectAsync(redis.GetConnectionString());
        await using var dbA = new BookingDbContext(options);
        await using var dbB = new BookingDbContext(options);
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var serviceA = new BookingService(dbA, new RedisSeatLock(connection), OpenSalesClient(), TimeProvider.System);
        var serviceB = new BookingService(dbB, new RedisSeatLock(connection), OpenSalesClient(), TimeProvider.System);

        var winner = await serviceA.CreateHoldAsync(userA, new CreateHoldRequest(eventId, [seats[4], seats[5], seats[6]]), CancellationToken.None);
        var loser = await serviceB.CreateHoldAsync(userB, new CreateHoldRequest(eventId, [seats[5], seats[6], seats[7]]), CancellationToken.None);
        var activeHold = await serviceA.CreateHoldAsync(userA, new CreateHoldRequest(eventId, [seats[7]]), CancellationToken.None);

        winner.Hold.Should().NotBeNull();
        loser.Hold.Should().BeNull();
        loser.Failure.Should().Be(HoldAttemptFailure.SeatsUnavailable);
        loser.UnavailableSeatIds.Should().BeEquivalentTo([seats[5], seats[6]]);
        activeHold.Failure.Should().Be(HoldAttemptFailure.ActiveHoldExists);
        activeHold.UnavailableSeatIds.Should().BeEmpty();

        await using (var verify = new BookingDbContext(options))
        {
            var inventory = await verify.Inventory.Where(x => x.EventId == eventId).ToDictionaryAsync(x => x.SeatId);
            inventory[seats[4]].HoldId.Should().Be(winner.Hold!.Id);
            inventory[seats[5]].HoldId.Should().Be(winner.Hold.Id);
            inventory[seats[6]].HoldId.Should().Be(winner.Hold.Id);
            inventory[seats[7]].Status.Should().Be(SeatInventoryStatus.Available);
            inventory[seats[7]].HoldId.Should().BeNull();
        }

        var retry = await serviceB.CreateHoldAsync(userB, new CreateHoldRequest(eventId, [seats[7]]), CancellationToken.None);
        retry.Hold.Should().NotBeNull();
    }

    [Fact]
    public async Task Sales_window_enforces_boundaries_and_preserves_valid_hold()
    {
        await using var postgres = new PostgreSqlBuilder().WithDatabase("flashseat_booking_sales_tests").Build();
        await using var redis = new RedisBuilder().Build();
        await Task.WhenAll(postgres.StartAsync(), redis.StartAsync());

        var options = new DbContextOptionsBuilder<BookingDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        var eventId = Guid.NewGuid();
        var seats = Enumerable.Range(1, 4).Select(_ => Guid.NewGuid()).ToArray();
        await using (var setup = new BookingDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Inventory.AddRange(seats.Select((seatId, index) => new EventSeatInventory(Guid.NewGuid(), eventId, seatId, "Main", "A", index + 1, 100, "VND")));
            await setup.SaveChangesAsync();
        }

        var start = new DateTimeOffset(2026, 7, 22, 10, 0, 0, TimeSpan.Zero);
        var end = start.AddHours(1);
        var clock = new TestTimeProvider(start.AddTicks(-1));
        await using var connection = await ConnectionMultiplexer.ConnectAsync(redis.GetConnectionString());
        await using var db = new BookingDbContext(options);
        var service = new BookingService(db, new RedisSeatLock(connection), SalesClient(start, end), clock);

        var before = await service.CreateHoldAsync(Guid.NewGuid(), new CreateHoldRequest(eventId, [seats[0]]), CancellationToken.None);
        before.Failure.Should().Be(HoldAttemptFailure.SalesNotOpen);

        clock.UtcNow = start;
        var atStart = await service.CreateHoldAsync(Guid.NewGuid(), new CreateHoldRequest(eventId, [seats[1]]), CancellationToken.None);
        atStart.Hold.Should().NotBeNull();

        clock.UtcNow = end.AddTicks(-1);
        var beforeEndUser = Guid.NewGuid();
        var beforeEnd = await service.CreateHoldAsync(beforeEndUser, new CreateHoldRequest(eventId, [seats[2]]), CancellationToken.None);
        beforeEnd.Hold.Should().NotBeNull();
        beforeEnd.Hold!.ExpiresAt.Should().Be(clock.UtcNow.AddMinutes(5));

        clock.UtcNow = end;
        var atEnd = await service.CreateHoldAsync(Guid.NewGuid(), new CreateHoldRequest(eventId, [seats[3]]), CancellationToken.None);
        atEnd.Failure.Should().Be(HoldAttemptFailure.SalesNotOpen);

        clock.UtcNow = end.AddMinutes(1);
        var booking = await service.CreateBookingAsync(beforeEndUser, new CreateBookingRequest(beforeEnd.Hold.Id), CancellationToken.None);
        booking.Should().NotBeNull("a hold accepted before sales close keeps its five-minute checkout window");

        await using var verify = new BookingDbContext(options);
        (await verify.Inventory.SingleAsync(x => x.SeatId == seats[0])).Status.Should().Be(SeatInventoryStatus.Available);
        (await verify.Inventory.SingleAsync(x => x.SeatId == seats[3])).Status.Should().Be(SeatInventoryStatus.Available);
    }

    [Fact]
    public async Task Events_failure_blocks_new_holds()
    {
        await using var postgres = new PostgreSqlBuilder().WithDatabase("flashseat_booking_sales_failure_tests").Build();
        await using var redis = new RedisBuilder().Build();
        await Task.WhenAll(postgres.StartAsync(), redis.StartAsync());
        var options = new DbContextOptionsBuilder<BookingDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        await using var db = new BookingDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await using var connection = await ConnectionMultiplexer.ConnectAsync(redis.GetConnectionString());
        var service = new BookingService(db, new RedisSeatLock(connection), Client(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)), TimeProvider.System);

        var result = await service.CreateHoldAsync(Guid.NewGuid(), new CreateHoldRequest(Guid.NewGuid(), [Guid.NewGuid()]), CancellationToken.None);

        result.Failure.Should().Be(HoldAttemptFailure.SalesWindowUnavailable);
        (await db.Holds.CountAsync()).Should().Be(0);
    }

    private static EventsClient OpenSalesClient() => SalesClient(DateTimeOffset.MinValue, DateTimeOffset.MaxValue);
    private static EventsClient SalesClient(DateTimeOffset start, DateTimeOffset end) => Client(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { salesStartAt = start, salesEndAt = end }), Encoding.UTF8, "application/json")
    });
    private static EventsClient Client(Func<HttpRequestMessage, HttpResponseMessage> response) =>
        new(new HttpClient(new TestHandler(response)) { BaseAddress = new Uri("http://events.test") });

    private static ConfigurationOptions RedisOptions(string connectionString, int database)
    {
        var options = ConfigurationOptions.Parse(connectionString);
        options.DefaultDatabase = database;
        return options;
    }

    private sealed class TestHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
