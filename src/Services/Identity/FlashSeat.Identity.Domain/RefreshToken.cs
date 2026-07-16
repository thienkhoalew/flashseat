namespace FlashSeat.Identity.Domain;

public sealed class RefreshToken
{
    private RefreshToken() { }

    public RefreshToken(Guid id, Guid userId, string tokenHash, DateTimeOffset expiresAt)
    {
        Id = id;
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public User User { get; private set; } = null!;

    public bool IsValid(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
