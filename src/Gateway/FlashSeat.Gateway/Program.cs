using FlashSeat.Observability;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.AddFlashSeatDefaults();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetValue<string>("WebOrigin") ?? "http://localhost:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();
app.UseFlashSeatDefaults();
if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/identity/swagger.json", "Identity");
        options.SwaggerEndpoint("/swagger/events/swagger.json", "Events");
        options.SwaggerEndpoint("/swagger/booking/swagger.json", "Booking");
        options.SwaggerEndpoint("/swagger/payment/swagger.json", "Payment");
    });
}
app.Use(async (context, next) =>
{
    const string header = "X-Correlation-ID";
    var correlationId = context.Request.Headers[header].FirstOrDefault();
    if (!Guid.TryParse(correlationId, out var parsed))
    {
        parsed = Guid.NewGuid();
    }

    context.Request.Headers[header] = parsed.ToString();
    context.Response.Headers[header] = parsed.ToString();
    await next(context);
});
app.UseCors();
app.UseRateLimiter();
app.MapReverseProxy();
app.Run();
