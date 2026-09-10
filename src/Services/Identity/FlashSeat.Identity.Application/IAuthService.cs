namespace FlashSeat.Identity.Application;

public interface IAuthService
{
    Task<RegisterResponse?> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
    Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<AuthResponse?> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken);
    Task<bool> ResendVerificationAsync(ResendVerificationRequest request, CancellationToken cancellationToken);
    Task<AuthResponse?> GoogleAuthAsync(GoogleAuthRequest request, CancellationToken cancellationToken);
    Task<AuthResponse?> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken);
    Task<bool> RevokeAsync(Guid userId, RevokeRequest request, CancellationToken cancellationToken);
    Task<CurrentUserResponse?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken);
}
