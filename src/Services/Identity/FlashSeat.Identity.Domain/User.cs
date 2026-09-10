namespace FlashSeat.Identity.Domain;

public sealed class User
{
    private User() { }

    public User(Guid id, string email, string fullName, DateTimeOffset createdAt)
    {
        Id = id;
        Email = email.Trim().ToUpperInvariant();
        FullName = fullName.Trim();
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    public UserRole Role { get; private set; } = UserRole.Customer;
    public bool IsActive { get; private set; } = true;
    public bool IsEmailVerified { get; private set; }
    public string? EmailVerificationCode { get; private set; }
    public DateTimeOffset? EmailVerificationCodeExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public ICollection<RefreshToken> RefreshTokens { get; } = [];

    public void SetPasswordHash(string passwordHash) => PasswordHash = passwordHash;
    public void SetRole(UserRole role) => Role = role;

    public void SetEmailVerified(bool verified)
    {
        IsEmailVerified = verified;
        if (verified)
        {
            EmailVerificationCode = null;
            EmailVerificationCodeExpiresAt = null;
        }
    }

    public void SetEmailVerificationCode(string code, DateTimeOffset expiresAt)
    {
        EmailVerificationCode = code;
        EmailVerificationCodeExpiresAt = expiresAt;
    }

    public bool VerifyEmail(string code, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(EmailVerificationCode) ||
            EmailVerificationCodeExpiresAt is null ||
            EmailVerificationCodeExpiresAt < now)
        {
            return false;
        }

        if (!string.Equals(EmailVerificationCode.Trim(), code.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        IsEmailVerified = true;
        EmailVerificationCode = null;
        EmailVerificationCodeExpiresAt = null;
        return true;
    }
}
