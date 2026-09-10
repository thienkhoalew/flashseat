using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FlashSeat.Identity.Application;
using FlashSeat.Identity.Domain;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FlashSeat.Identity.Infrastructure;

public sealed class AuthService(
    IdentityDbContext dbContext,
    IPasswordHasher<User> passwordHasher,
    IOptions<JwtOptions> jwtOptions,
    IOptions<GoogleOptions> googleOptions,
    IEmailSender emailSender,
    TimeProvider timeProvider) : IAuthService
{
    private readonly JwtOptions _jwt = jwtOptions.Value;
    private readonly GoogleOptions _google = googleOptions.Value;

    public async Task<RegisterResponse?> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        if (await dbContext.Users.AnyAsync(x => x.Email == normalizedEmail, cancellationToken))
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var user = new User(Guid.NewGuid(), request.Email, request.FullName, now);
        user.SetPasswordHash(passwordHasher.HashPassword(user, request.Password));
        user.SetEmailVerified(false);

        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString("D6");
        user.SetEmailVerificationCode(code, now.AddMinutes(15));
        dbContext.Users.Add(user);

        await dbContext.SaveChangesAsync(cancellationToken);
        await emailSender.SendVerificationEmailAsync(request.Email.Trim(), request.FullName.Trim(), code, cancellationToken);

        return new RegisterResponse(user.Email.ToLowerInvariant(), "Verification code sent to your email.");
    }

    public async Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Email == normalizedEmail, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return new LoginResult(null);
        }

        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            return new LoginResult(null);
        }

        if (!user.IsEmailVerified)
        {
            return new LoginResult(null, IsUnverified: true, Email: user.Email.ToLowerInvariant());
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.SetPasswordHash(passwordHasher.HashPassword(user, request.Password));
        }

        var response = CreateTokens(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new LoginResult(response);
    }

    public async Task<AuthResponse?> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Email == normalizedEmail, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return null;
        }

        if (user.IsEmailVerified)
        {
            var existingResponse = CreateTokens(user);
            await dbContext.SaveChangesAsync(cancellationToken);
            return existingResponse;
        }

        var now = timeProvider.GetUtcNow();
        if (!user.VerifyEmail(request.Code, now))
        {
            return null;
        }

        var response = CreateTokens(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        return response;
    }

    public async Task<bool> ResendVerificationAsync(ResendVerificationRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Email == normalizedEmail, cancellationToken);
        if (user is null || !user.IsActive || user.IsEmailVerified)
        {
            return true;
        }

        var now = timeProvider.GetUtcNow();
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString("D6");
        user.SetEmailVerificationCode(code, now.AddMinutes(15));
        await dbContext.SaveChangesAsync(cancellationToken);

        await emailSender.SendVerificationEmailAsync(user.Email, user.FullName, code, cancellationToken);
        return true;
    }

    public async Task<AuthResponse?> GoogleAuthAsync(GoogleAuthRequest request, CancellationToken cancellationToken)
    {
        GoogleJsonWebSignature.Payload payload;
        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings();
            if (!string.IsNullOrWhiteSpace(_google.ClientId))
            {
                settings.Audience = [_google.ClientId];
            }

            payload = await GoogleJsonWebSignature.ValidateAsync(request.IdToken, settings);
        }
        catch
        {
            return null;
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Email) || !payload.EmailVerified)
        {
            return null;
        }

        var normalizedEmail = payload.Email.Trim().ToUpperInvariant();
        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Email == normalizedEmail, cancellationToken);
        if (user is null)
        {
            var fullName = string.IsNullOrWhiteSpace(payload.Name)
                ? payload.Email.Split('@')[0]
                : payload.Name.Trim();

            user = new User(Guid.NewGuid(), payload.Email, fullName, timeProvider.GetUtcNow());
            user.SetPasswordHash(passwordHasher.HashPassword(user, Convert.ToHexString(RandomNumberGenerator.GetBytes(32))));
            user.SetEmailVerified(true);
            dbContext.Users.Add(user);
        }
        else if (!user.IsActive)
        {
            return null;
        }
        else if (!user.IsEmailVerified)
        {
            user.SetEmailVerified(true);
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
            new Claim(ClaimTypes.GivenName, user.FullName),
            new Claim("full_name", user.FullName),
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
