using System.Security.Claims;
using FlashSeat.Observability;
using FlashSeat.Payment.Application;
using FlashSeat.Payment.Infrastructure;
using FluentValidation;

var builder = WebApplication.CreateBuilder(args);
builder.AddFlashSeatDefaults();
builder.Services.AddPaymentInfrastructure(builder.Configuration);
builder.Services.AddValidatorsFromAssemblyContaining<CreatePaymentRequestValidator>();
builder.Services.AddFlashSeatSwagger();

var app = builder.Build();
if (app.Environment.IsDevelopment()) await app.Services.InitializePaymentDatabaseAsync(useMigrations: false);
app.UseFlashSeatDefaults();
app.UseAuthentication();
app.UseAuthorization();
if (app.Environment.IsDevelopment()) app.UseSwagger();

app.MapPost("/api/payments", async (CreatePaymentRequest request, HttpRequest http, ClaimsPrincipal user, IValidator<CreatePaymentRequest> validator, IPaymentService service, CancellationToken ct) =>
{
    if (!http.Headers.TryGetValue("Idempotency-Key", out var key) || !Guid.TryParse(key, out _))
        return Results.BadRequest<object>(new { title = "Valid Idempotency-Key header is required." });
    var validation = await validator.ValidateAsync(request, ct);
    if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());
    try
    {
        var result = await service.CreateAsync(UserId(user), key.ToString(), request, ct);
        return result.IdempotencyConflict ? Results.Conflict() : result.Payment is null
            ? Results.Json(new { title = "Payment provider is unavailable. Please try again." }, statusCode: StatusCodes.Status502BadGateway)
            : Results.Created($"/api/payments/{result.Payment.Id}", result.Payment);
    }
    catch (HttpRequestException)
    {
        return Results.Json(new { title = "Payment provider is unavailable. Please try again." }, statusCode: StatusCodes.Status502BadGateway);
    }
    catch (InvalidOperationException exception) when (exception.Message.Contains("PayOS", StringComparison.OrdinalIgnoreCase))
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
}).RequireAuthorization();

app.MapGet("/api/payments/{paymentId:guid}", async (Guid paymentId, ClaimsPrincipal user, IPaymentService service, CancellationToken ct) =>
    await service.GetAsync(UserId(user), user.IsInRole("Admin"), paymentId, ct) is { } result ? Results.Ok(result) : Results.NotFound()
).RequireAuthorization();

app.MapPost("/api/payments/payos-webhook", async (PayOSWebhookRequest request, IPaymentService service, CancellationToken ct) =>
{
    var success = await service.ProcessPayOSWebhookAsync(request, ct);
    return success ? Results.Ok(new { code = "00", message = "Success" }) : Results.BadRequest(new { code = "01", message = "Processing failed" });
}).AllowAnonymous();

app.Run();

static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
public partial class Program;
