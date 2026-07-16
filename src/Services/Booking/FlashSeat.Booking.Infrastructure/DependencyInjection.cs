using FlashSeat.Booking.Application;
using FlashSeat.Observability;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace FlashSeat.Booking.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBookingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("BookingDb") ?? throw new InvalidOperationException("BookingDb is required.");
        var redisConnection = configuration.GetConnectionString("Redis") ?? throw new InvalidOperationException("Redis is required.");
        services.AddDbContext<BookingDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnection));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<RedisSeatLock>();
        services.AddScoped<IBookingService, BookingService>();
        services.AddHostedService<ExpiredHoldWorker>();
        services.AddMassTransit(x =>
        {
            x.AddConsumer<PaymentSucceededConsumer>();
            x.AddConsumer<PaymentFailedConsumer>();
            x.AddEntityFrameworkOutbox<BookingDbContext>(o => { o.UsePostgres(); o.UseBusOutbox(); });
            x.AddConfigureEndpointsCallback((context, _, cfg) =>
            {
                cfg.UseEntityFrameworkOutbox<BookingDbContext>(context);
                cfg.UseMessageRetry(r => r.Exponential(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(2)));
            });
            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(configuration["RabbitMq:Host"] ?? "rabbitmq", "/", h =>
                {
                    h.Username(configuration["RabbitMq:Username"] ?? "flashseat");
                    h.Password(configuration["RabbitMq:Password"] ?? "flashseat-local");
                });
                cfg.ConfigureEndpoints(context);
            });
        });
        services.AddFlashSeatAuthentication(configuration);
        services.AddHealthChecks().AddNpgSql(connectionString, tags: ["ready"]).AddRedis(redisConnection, tags: ["ready"]);
        return services;
    }
}
