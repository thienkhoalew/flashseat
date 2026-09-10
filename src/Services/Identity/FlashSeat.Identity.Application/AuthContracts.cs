namespace FlashSeat.Identity.Application;

public sealed record RegisterRequest(string Email, string Password, string FullName);
public sealed record RegisterResponse(string Email, string Message);
public sealed record VerifyEmailRequest(string Email, string Code);
public sealed record ResendVerificationRequest(string Email);
public sealed record LoginRequest(string Email, string Password);
public sealed record LoginResult(AuthResponse? Response, bool IsUnverified = false, string? Email = null);
public sealed record GoogleAuthRequest(string IdToken);
public sealed record RefreshRequest(string RefreshToken);
public sealed record RevokeRequest(string RefreshToken);

public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed record CurrentUserResponse(Guid Id, string Email, string FullName, string Role);
