using System.Globalization;
using FlashSeat.Notification.Worker;
using MassTransit;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSerilog(configuration => configuration.WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));
builder.Services.AddOptions<NotificationWorkerOptions>()
    .Bind(builder.Configuration.GetSection(NotificationWorkerOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<NotificationBuffer>();
builder.Services.AddHostedService<NotificationProcessor>();
builder.Services.AddMassTransit(bus =>
{
    bus.AddConsumer<BookingConfirmedConsumer>();
    bus.AddConsumer<BookingCancelledConsumer>();
    bus.UsingRabbitMq((context, rabbit) =>
    {
        rabbit.Host(builder.Configuration["RabbitMq:Host"] ?? "rabbitmq", "/", host =>
        {
            host.Username(builder.Configuration["RabbitMq:Username"] ?? "flashseat");
            host.Password(builder.Configuration["RabbitMq:Password"] ?? "flashseat-local");
        });
        rabbit.UseMessageRetry(retry => retry.Exponential(
            3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2)));
        rabbit.ConfigureEndpoints(context);
    });
});
await builder.Build().RunAsync();
