using FlashSeat.Events.Application;
using FlashSeat.Events.Infrastructure;
using FlashSeat.Observability;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddFlashSeatDefaults();
builder.Services.AddEventsInfrastructure(builder.Configuration);
builder.Services.AddValidatorsFromAssemblyContaining<SaveEventRequestValidator>();
builder.Services.AddFlashSeatSwagger();

var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<EventsDbContext>();
    await db.Database.MigrateAsync();
}
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
app.MapGet("/internal/events/{eventId:guid}/metadata", async (Guid eventId, IEventService service, CancellationToken cancellationToken) =>
    await service.GetMetadataAsync(eventId, cancellationToken) is { } result
        ? Results.Ok(result) : Results.NotFound()).ExcludeFromDescription();

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
    try
    {
        var result = await service.UpdateAsync(eventId, request, cancellationToken);
        return result is null ? Results.NotFound(new { code = "event_not_found", title = "Event not found." }) : Results.Ok(result);
    }
    catch (EventLifecycleException exception)
    {
        return Results.Json(new { code = exception.Code, title = exception.Message, status = exception.StatusCode }, statusCode: exception.StatusCode);
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new { code = "invalid_transition", title = exception.Message, status = StatusCodes.Status409Conflict });
    }
});

admin.MapPost("/{eventId:guid}/publish", async (Guid eventId, IEventService service, CancellationToken cancellationToken) =>
    await RunLifecycleAsync(() => service.PublishAsync(eventId, cancellationToken)));
admin.MapPost("/{eventId:guid}/cancel", async (Guid eventId, IEventService service, CancellationToken cancellationToken) =>
    await RunLifecycleAsync(() => service.CancelAsync(eventId, cancellationToken)));
admin.MapPost("/{eventId:guid}/unpublish", async (Guid eventId, IEventService service, CancellationToken cancellationToken) =>
    await RunLifecycleAsync(() => service.UnpublishAsync(eventId, cancellationToken)));
admin.MapPost("/{eventId:guid}/restore-draft", async (Guid eventId, IEventService service, CancellationToken cancellationToken) =>
    await RunLifecycleAsync(() => service.RestoreDraftAsync(eventId, cancellationToken)));
admin.MapPost("/{eventId:guid}/republish", async (Guid eventId, IEventService service, CancellationToken cancellationToken) =>
    await RunLifecycleAsync(() => service.RepublishAsync(eventId, cancellationToken)));
admin.MapDelete("/{eventId:guid}", async (Guid eventId, IEventService service, CancellationToken cancellationToken) =>
    await RunLifecycleAsync(() => service.ArchiveAsync(eventId, cancellationToken)));

app.Run();

static async Task<IResult> RunLifecycleAsync(Func<Task<bool>> action)
{
    try
    {
        return await action() ? Results.NoContent() : Results.NotFound(new { code = "event_not_found", title = "Event not found." });
    }
    catch (EventLifecycleException exception)
    {
        return Results.Json(new { code = exception.Code, title = exception.Message, status = exception.StatusCode }, statusCode: exception.StatusCode);
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new { code = "invalid_transition", title = exception.Message, status = StatusCodes.Status409Conflict });
    }
}

public partial class Program;
