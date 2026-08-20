using System.Security.Claims;
using FlashSeat.Booking.Api;
using FlashSeat.Booking.Application;
using FlashSeat.Booking.Infrastructure;
using FlashSeat.Observability;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);
builder.AddFlashSeatDefaults();
builder.Services.AddBookingInfrastructure(builder.Configuration);
builder.Services.AddValidatorsFromAssemblyContaining<CreateHoldRequestValidator>();
builder.Services.AddSignalR();
builder.Services.AddFlashSeatSwagger();
var app = builder.Build();
await app.Services.InitializeBookingDatabaseAsync();
app.UseFlashSeatDefaults(); app.UseAuthentication(); app.UseAuthorization();
if (app.Environment.IsDevelopment()) app.UseSwagger();

app.MapGet("/api/events/{eventId:guid}/availability", async (Guid eventId, IBookingService service, CancellationToken ct) => Results.Ok(await service.GetAvailabilityAsync(eventId, ct))).AllowAnonymous();
app.MapPost("/internal/events/inventory-summary", async (InventorySummaryRequest request, IBookingService service, CancellationToken ct) => Results.Ok(await service.GetInventorySummariesAsync(request.EventIds, ct))).AllowAnonymous().ExcludeFromDescription();
app.MapPost("/api/seat-holds", async (CreateHoldRequest request, ClaimsPrincipal user, IValidator<CreateHoldRequest> validator, IBookingService service, IHubContext<SeatAvailabilityHub> hub, CancellationToken ct) =>
{
    var validation = await validator.ValidateAsync(request, ct); if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());
    var result = await service.CreateHoldAsync(UserId(user), request, ct);
    if (result.Hold is null)
    {
        var title = result.Failure switch
        {
            HoldAttemptFailure.ActiveHoldExists => "You already have an active hold for this event.",
            HoldAttemptFailure.LockContention => "Seat selection is being updated. Try again.",
            HoldAttemptFailure.SalesNotOpen => "Ticket sales are not open for this event.",
            HoldAttemptFailure.SalesWindowUnavailable => "Ticket sales are temporarily unavailable. Try again.",
            _ => "Seats unavailable"
        };
        return result.Failure == HoldAttemptFailure.SalesWindowUnavailable
            ? Results.Json(new { title, unavailableSeatIds = result.UnavailableSeatIds }, statusCode: StatusCodes.Status503ServiceUnavailable)
            : Results.Conflict(new { title, unavailableSeatIds = result.UnavailableSeatIds });
    }
    await hub.Clients.Group($"event:{request.EventId:N}").SendAsync("SeatsHeld", new { eventId = request.EventId, seatIds = request.SeatIds, status = "Held", timestamp = DateTimeOffset.UtcNow }, ct);
    return Results.Created($"/api/seat-holds/{result.Hold.Id}", result.Hold);
}).RequireAuthorization();
app.MapGet("/api/seat-holds/{holdId:guid}", async (Guid holdId, ClaimsPrincipal user, IBookingService service, CancellationToken ct) => await service.GetHoldAsync(UserId(user), holdId, ct) is { } result ? Results.Ok(result) : Results.NotFound()).RequireAuthorization();
app.MapDelete("/api/seat-holds/{holdId:guid}", async (Guid holdId, ClaimsPrincipal user, IBookingService service, IHubContext<SeatAvailabilityHub> hub, CancellationToken ct) =>
{
    var hold = await service.GetHoldAsync(UserId(user), holdId, ct);
    if (hold is null || !await service.ReleaseHoldAsync(UserId(user), holdId, ct)) return Results.NotFound();
    await hub.Clients.Group($"event:{hold.EventId:N}").SendAsync("SeatsReleased", new { eventId = hold.EventId, seatIds = hold.Items.Select(x => x.SeatId), status = "Available", timestamp = DateTimeOffset.UtcNow }, ct);
    return Results.NoContent();
}).RequireAuthorization();
app.MapPost("/api/bookings", async (CreateBookingRequest request, ClaimsPrincipal user, IValidator<CreateBookingRequest> validator, IBookingService service, CancellationToken ct) =>
{
    var validation = await validator.ValidateAsync(request, ct); if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());
    return await service.CreateBookingAsync(UserId(user), request, ct) is { } result ? Results.Created($"/api/bookings/{result.Id}", result) : Results.Conflict();
}).RequireAuthorization();
app.MapGet("/api/bookings/me", async (ClaimsPrincipal user, IBookingService service, CancellationToken ct) => Results.Ok(await service.GetBookingsAsync(UserId(user), ct))).RequireAuthorization();
app.MapGet("/api/bookings/{bookingId:guid}", async (Guid bookingId, ClaimsPrincipal user, IBookingService service, CancellationToken ct) => await service.GetBookingAsync(UserId(user), user.IsInRole("Admin"), bookingId, ct) is { } result ? Results.Ok(result) : Results.NotFound()).RequireAuthorization();
app.MapPost("/api/admin/check-ins", async (CheckInRequest request, ClaimsPrincipal user, IBookingService service, CancellationToken ct) =>
{
    var result = await service.CheckInAsync(UserId(user), request.TicketCode, ct);
    return result.Failure switch
    {
        CheckInFailure.UnknownTicket => Results.NotFound(new { title = "Ticket not found." }),
        CheckInFailure.BookingNotConfirmed => Results.Conflict(new { title = "Booking is not confirmed." }),
        CheckInFailure.AlreadyCheckedIn => Results.Conflict(result.Response),
        _ => Results.Ok(result.Response)
    };
}).RequireAuthorization(policy => policy.RequireRole("Admin"));
app.MapGet("/internal/events/{eventId:guid}/activity", async (Guid eventId, IBookingService service, CancellationToken ct) =>
    Results.Ok(await service.GetEventActivityAsync(eventId, ct))).AllowAnonymous().ExcludeFromDescription();
app.MapPut("/internal/events/inventory", async (InventoryReplacementRequest request, IBookingService service, CancellationToken ct) =>
{
    await service.ReplaceInventoryAsync(request, ct);
    return Results.NoContent();
}).AllowAnonymous().ExcludeFromDescription();
app.MapPost("/internal/events/inventory", async (InventoryImportRequest request, IBookingService service, CancellationToken ct) => { await service.ImportInventoryAsync(request, ct); return Results.NoContent(); }).ExcludeFromDescription();
app.MapGet("/internal/bookings/{bookingId:guid}", async (Guid bookingId, ClaimsPrincipal user, BookingDbContext db, CancellationToken ct) =>
{
    var userId = UserId(user);
    var result = await db.Bookings.AsNoTracking().Where(x => x.Id == bookingId && x.UserId == userId)
        .Select(x => new { x.Id, x.UserId, x.TotalAmount, x.Currency, Status = x.Status.ToString(), PaymentDueAt = db.Holds.Where(h => h.Id == x.HoldId).Select(h => h.ExpiresAt).Single() })
        .SingleOrDefaultAsync(ct);
    return result is null ? Results.NotFound() : Results.Ok(result);
}).RequireAuthorization().ExcludeFromDescription();
app.MapHub<SeatAvailabilityHub>("/hubs/seat-availability");
app.Run();
static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
public partial class Program;
