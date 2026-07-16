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
    public DateTimeOffset CreatedAt { get; private set; }
    public ICollection<RefreshToken> RefreshTokens { get; } = [];

    public void SetPasswordHash(string passwordHash) => PasswordHash = passwordHash;
    public void SetRole(UserRole role) => Role = role;
}
