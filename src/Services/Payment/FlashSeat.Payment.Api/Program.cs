using System.Security.Claims;
using FlashSeat.Observability;
using FlashSeat.Payment.Application;
using FlashSeat.Payment.Infrastructure;
using FluentValidation;
var builder = WebApplication.CreateBuilder(args); builder.AddFlashSeatDefaults(); builder.Services.AddPaymentInfrastructure(builder.Configuration); builder.Services.AddValidatorsFromAssemblyContaining<CreatePaymentRequestValidator>(); builder.Services.AddFlashSeatSwagger();
var app = builder.Build(); if (app.Environment.IsDevelopment()) await app.Services.InitializePaymentDatabaseAsync(); app.UseFlashSeatDefaults(); app.UseAuthentication(); app.UseAuthorization(); if (app.Environment.IsDevelopment()) app.UseSwagger();
app.MapPost("/api/payments", async (CreatePaymentRequest request, HttpRequest http, ClaimsPrincipal user, IValidator<CreatePaymentRequest> validator, IPaymentService service, CancellationToken ct) =>
{ if (!http.Headers.TryGetValue("Idempotency-Key", out var key) || !Guid.TryParse(key, out _)) return Results.BadRequest(new { title = "Valid Idempotency-Key header is required." }); var validation = await validator.ValidateAsync(request, ct); if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary()); var result = await service.CreateAsync(UserId(user), key.ToString(), request, ct); return result.IdempotencyConflict ? Results.Conflict() : Results.Created($"/api/payments/{result.Payment!.Id}", result.Payment); }).RequireAuthorization();
app.MapGet("/api/payments/{paymentId:guid}", async (Guid paymentId, ClaimsPrincipal user, IPaymentService service, CancellationToken ct) => await service.GetAsync(UserId(user), user.IsInRole("Admin"), paymentId, ct) is { } result ? Results.Ok(result) : Results.NotFound()).RequireAuthorization();
app.Run(); static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!); public partial class Program;
