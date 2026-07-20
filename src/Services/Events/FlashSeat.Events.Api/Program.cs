using FlashSeat.Events.Application;
using FlashSeat.Events.Infrastructure;
using FlashSeat.Observability;
using FluentValidation;

var builder = WebApplication.CreateBuilder(args);
builder.AddFlashSeatDefaults();
builder.Services.AddEventsInfrastructure(builder.Configuration);
builder.Services.AddValidatorsFromAssemblyContaining<SaveEventRequestValidator>();
builder.Services.AddFlashSeatSwagger();

var app = builder.Build();
if (app.Environment.IsDevelopment()) await app.Services.SeedEventsAsync();
app.UseFlashSeatDefaults();
app.UseAuthentication();
app.UseAuthorization();
if (app.Environment.IsDevelopment()) app.UseSwagger();

app.MapGet("/api/events", async (string? search, DateTimeOffset? from, DateTimeOffset? to, int? page,
    int? pageSize, string? sort, IEventService service, CancellationToken cancellationToken) =>
{
    var currentPage = Math.Max(1, page ?? 1);
    var currentPageSize = Math.Clamp(pageSize ?? 12, 1, 50);
    return Results.Ok(await service.GetEventsAsync(search, from, to, currentPage, currentPageSize,
        sort == "createdAt" ? sort : "startsAt", false, cancellationToken));
}).AllowAnonymous();

app.MapGet("/api/events/{eventId:guid}", async (Guid eventId, IEventService service, CancellationToken cancellationToken) =>
    await service.GetEventAsync(eventId, false, cancellationToken) is { } result
        ? Results.Ok(result) : Results.NotFound()).AllowAnonymous();

app.MapGet("/api/events/{eventId:guid}/seats", async (Guid eventId, IEventService service, CancellationToken cancellationToken) =>
    await service.GetEventAsync(eventId, false, cancellationToken) is { } result
        ? Results.Ok(result.Seats) : Results.NotFound()).AllowAnonymous();

var admin = app.MapGroup("/api/admin/events").RequireAuthorization(policy => policy.RequireRole("Admin"));
admin.MapGet("/", async (string? search, int page, int pageSize, IEventService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GetEventsAsync(search, null, null, Math.Max(1, page == 0 ? 1 : page),
        Math.Clamp(pageSize == 0 ? 12 : pageSize, 1, 50), "createdAt", true, cancellationToken)));
admin.MapGet("/{eventId:guid}", async (Guid eventId, IEventService service, CancellationToken cancellationToken) =>
    await service.GetEventAsync(eventId, true, cancellationToken) is { } result
        ? Results.Ok(result) : Results.NotFound());

admin.MapPost("/", async (SaveEventRequest request, IValidator<SaveEventRequest> validator,
    IEventService service, CancellationToken cancellationToken) =>
{
    var validation = await validator.ValidateAsync(request, cancellationToken);
    if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());
    var result = await service.CreateAsync(request, cancellationToken);
    return result is null ? Results.Conflict() : Results.Created($"/api/admin/events/{result.Id}", result);
});

admin.MapPut("/{eventId:guid}", async (Guid eventId, SaveEventRequest request,
    IValidator<SaveEventRequest> validator, IEventService service, CancellationToken cancellationToken) =>
{
    var validation = await validator.ValidateAsync(request, cancellationToken);
    if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());
    var result = await service.UpdateAsync(eventId, request, cancellationToken);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

admin.MapPost("/{eventId:guid}/publish", async (Guid eventId, IEventService service, CancellationToken cancellationToken) =>
    await service.PublishAsync(eventId, cancellationToken) ? Results.NoContent() : Results.NotFound());
admin.MapPost("/{eventId:guid}/cancel", async (Guid eventId, IEventService service, CancellationToken cancellationToken) =>
    await service.CancelAsync(eventId, cancellationToken) ? Results.NoContent() : Results.NotFound());

app.Run();
public partial class Program;
