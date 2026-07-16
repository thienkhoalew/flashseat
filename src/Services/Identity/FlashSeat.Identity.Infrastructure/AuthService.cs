using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FlashSeat.Identity.Application;
using FlashSeat.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FlashSeat.Identity.Infrastructure;

public sealed class AuthService(
    IdentityDbContext dbContext,
    IPasswordHasher<User> passwordHasher,
    IOptions<JwtOptions> jwtOptions,
    TimeProvider timeProvider) : IAuthService
{
    private readonly JwtOptions _jwt = jwtOptions.Value;

    public async Task<AuthResponse?> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        if (await dbContext.Users.AnyAsync(x => x.Email == normalizedEmail, cancellationToken))
        {
            return null;
        }

        var user = new User(Guid.NewGuid(), request.Email, request.FullName, timeProvider.GetUtcNow());
        user.SetPasswordHash(passwordHasher.HashPassword(user, request.Password));
        dbContext.Users.Add(user);

        var response = CreateTokens(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        return response;
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Email == normalizedEmail, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return null;
        }

        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            return null;
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.SetPasswordHash(passwordHasher.HashPassword(user, request.Password));
        }

        var response = CreateTokens(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        return response;
    }

    public async Task<AuthResponse?> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var tokenHash = HashToken(request.RefreshToken);
        var oldToken = await dbContext.RefreshTokens
            .Include(x => x.User)
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

        if (oldToken is null || !oldToken.IsValid(now) || !oldToken.User.IsActive)
        {
            return null;
        }

        oldToken.Revoke(now);
        var response = CreateTokens(oldToken.User);
        await dbContext.SaveChangesAsync(cancellationToken);
        return response;
    }

    public async Task<bool> RevokeAsync(Guid userId, RevokeRequest request, CancellationToken cancellationToken)
    {
        var tokenHash = HashToken(request.RefreshToken);
        var token = await dbContext.RefreshTokens
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash && x.UserId == userId, cancellationToken);
        if (token is null)
        {
            return false;
        }

        token.Revoke(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<CurrentUserResponse?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.Users
            .Where(x => x.Id == userId && x.IsActive)
            .Select(x => new CurrentUserResponse(x.Id, x.Email.ToLowerInvariant(), x.FullName, x.Role.ToString()))
            .SingleOrDefaultAsync(cancellationToken);

    private AuthResponse CreateTokens(User user)
    {
        var now = timeProvider.GetUtcNow();
        var accessExpiresAt = now.AddMinutes(_jwt.AccessTokenMinutes);
        var refreshExpiresAt = now.AddDays(_jwt.RefreshTokenDays);
        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        dbContext.RefreshTokens.Add(new RefreshToken(
            Guid.NewGuid(), user.Id, HashToken(refreshToken), refreshExpiresAt));

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email.ToLowerInvariant()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Email.ToLowerInvariant()),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            _jwt.Issuer,
            _jwt.Audience,
            claims,
            now.UtcDateTime,
            accessExpiresAt.UtcDateTime,
            credentials);

        return new AuthResponse(
            new JwtSecurityTokenHandler().WriteToken(jwt), accessExpiresAt, refreshToken, refreshExpiresAt);
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
