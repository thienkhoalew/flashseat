using System.Security.Claims;
using FlashSeat.Identity.Application;
using FlashSeat.Identity.Infrastructure;
using FlashSeat.Observability;
using FluentValidation;

var builder = WebApplication.CreateBuilder(args);

builder.AddFlashSeatDefaults();
builder.Services.AddIdentityInfrastructure(builder.Configuration);
builder.Services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();
builder.Services.AddFlashSeatSwagger();

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    await app.Services.SeedIdentityAsync();
}

app.UseFlashSeatDefaults();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
}

var auth = app.MapGroup("/api/auth").WithTags("Authentication");

auth.MapPost("/register", async (
    RegisterRequest request,
    IValidator<RegisterRequest> validator,
    IAuthService service,
    CancellationToken cancellationToken) =>
{
    var validation = await validator.ValidateAsync(request, cancellationToken);
    if (!validation.IsValid)
    {
        return Results.ValidationProblem(validation.ToDictionary());
    }

    var response = await service.RegisterAsync(request, cancellationToken);
    return response is null
        ? Results.Conflict(new { title = "Unable to create account." })
        : Results.Ok(response);
}).AllowAnonymous();

auth.MapPost("/login", async (
    LoginRequest request,
    IValidator<LoginRequest> validator,
    IAuthService service,
    CancellationToken cancellationToken) =>
{
    var validation = await validator.ValidateAsync(request, cancellationToken);
    if (!validation.IsValid)
    {
        return Results.ValidationProblem(validation.ToDictionary());
    }

    var result = await service.LoginAsync(request, cancellationToken);
    if (result.IsUnverified)
    {
        return Results.Json(
            new { title = "Email not verified.", code = "EMAIL_NOT_VERIFIED", email = result.Email },
            statusCode: StatusCodes.Status403Forbidden);
    }

    return result.Response is null ? Results.Unauthorized() : Results.Ok(result.Response);
}).AllowAnonymous();

auth.MapPost("/verify-email", async (
    VerifyEmailRequest request,
    IValidator<VerifyEmailRequest> validator,
    IAuthService service,
    CancellationToken cancellationToken) =>
{
    var validation = await validator.ValidateAsync(request, cancellationToken);
    if (!validation.IsValid)
    {
        return Results.ValidationProblem(validation.ToDictionary());
    }

    var response = await service.VerifyEmailAsync(request, cancellationToken);
    return response is null
        ? Results.BadRequest(new { title = "Invalid or expired verification code." })
        : Results.Ok(response);
}).AllowAnonymous();

auth.MapPost("/resend-verification", async (
    ResendVerificationRequest request,
    IValidator<ResendVerificationRequest> validator,
    IAuthService service,
    CancellationToken cancellationToken) =>
{
    var validation = await validator.ValidateAsync(request, cancellationToken);
    if (!validation.IsValid)
    {
        return Results.ValidationProblem(validation.ToDictionary());
    }

    await service.ResendVerificationAsync(request, cancellationToken);
    return Results.Ok(new { message = "If the account exists and is unverified, a new code has been sent." });
}).AllowAnonymous();

auth.MapPost("/google", async (
    GoogleAuthRequest request,
    IValidator<GoogleAuthRequest> validator,
    IAuthService service,
    CancellationToken cancellationToken) =>
{
    var validation = await validator.ValidateAsync(request, cancellationToken);
    if (!validation.IsValid)
    {
        return Results.ValidationProblem(validation.ToDictionary());
    }

    var response = await service.GoogleAuthAsync(request, cancellationToken);
    return response is null ? Results.Unauthorized() : Results.Ok(response);
}).AllowAnonymous();

auth.MapPost("/refresh", async (
    RefreshRequest request,
    IValidator<RefreshRequest> validator,
    IAuthService service,
    CancellationToken cancellationToken) =>
{
    var validation = await validator.ValidateAsync(request, cancellationToken);
    if (!validation.IsValid)
    {
        return Results.ValidationProblem(validation.ToDictionary());
    }

    var response = await service.RefreshAsync(request, cancellationToken);
    return response is null ? Results.Unauthorized() : Results.Ok(response);
}).AllowAnonymous();

auth.MapPost("/revoke", async (
    RevokeRequest request,
    ClaimsPrincipal principal,
    IValidator<RevokeRequest> validator,
    IAuthService service,
    CancellationToken cancellationToken) =>
{
    var validation = await validator.ValidateAsync(request, cancellationToken);
    if (!validation.IsValid)
    {
        return Results.ValidationProblem(validation.ToDictionary());
    }

    var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    return await service.RevokeAsync(userId, request, cancellationToken)
        ? Results.NoContent()
        : Results.NotFound();
}).RequireAuthorization();

auth.MapGet("/me", async (
    ClaimsPrincipal principal,
    IAuthService service,
    CancellationToken cancellationToken) =>
{
    var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var response = await service.GetCurrentUserAsync(userId, cancellationToken);
    return response is null ? Results.NotFound() : Results.Ok(response);
}).RequireAuthorization();

app.Run();

public partial class Program;
