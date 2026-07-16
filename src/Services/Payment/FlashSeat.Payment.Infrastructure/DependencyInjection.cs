using FlashSeat.Observability;
using FlashSeat.Payment.Application;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace FlashSeat.Payment.Infrastructure;
public static class DependencyInjection
{
    public static IServiceCollection AddPaymentInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("PaymentDb") ?? throw new InvalidOperationException("PaymentDb is required.");
        services.AddDbContext<PaymentDbContext>(x => x.UseNpgsql(connection)); services.AddSingleton(TimeProvider.System); services.AddScoped<IPaymentService, PaymentService>();
        services.AddHttpContextAccessor();
        services.AddHttpClient("booking", client => client.BaseAddress = new Uri(configuration["Services:Booking"] ?? "http://booking-api:8080"));
        services.AddMassTransit(x =>
        {
            x.AddEntityFrameworkOutbox<PaymentDbContext>(o => { o.UsePostgres(); o.UseBusOutbox(); });
            x.UsingRabbitMq((context, cfg) => cfg.Host(configuration["RabbitMq:Host"] ?? "rabbitmq", "/", h => { h.Username(configuration["RabbitMq:Username"] ?? "flashseat"); h.Password(configuration["RabbitMq:Password"] ?? "flashseat-local"); }));
        });
        services.AddFlashSeatAuthentication(configuration); services.AddHealthChecks().AddNpgSql(connection, tags: ["ready"]); return services;
    }
    public static async Task InitializePaymentDatabaseAsync(this IServiceProvider services)
    { await using var scope = services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<PaymentDbContext>().Database.EnsureCreatedAsync(); }
}
