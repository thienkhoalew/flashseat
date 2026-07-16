using FlashSeat.Events.Application;
using FlashSeat.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FlashSeat.Events.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddEventsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("EventsDb")
            ?? throw new InvalidOperationException("ConnectionStrings:EventsDb is required.");
        services.AddDbContext<EventsDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton(TimeProvider.System);
        services.AddHttpClient<BookingInventoryClient>(client =>
            client.BaseAddress = new Uri(configuration["Services:Booking"] ?? "http://booking-api:8080"));
        services.AddScoped<IEventService, EventService>();
        services.AddFlashSeatAuthentication(configuration);
        services.AddHealthChecks().AddNpgSql(connectionString, tags: ["ready"]);
        return services;
    }
}
