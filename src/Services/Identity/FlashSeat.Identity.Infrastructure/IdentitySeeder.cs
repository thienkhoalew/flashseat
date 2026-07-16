using FlashSeat.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlashSeat.Identity.Infrastructure;

public static class IdentitySeeder
{
    public static async Task SeedIdentityAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        await dbContext.Database.MigrateAsync(cancellationToken);

        await EnsureUserAsync(
            dbContext, hasher, "admin@flashseat.dev", "Admin@123456", "FlashSeat Admin", UserRole.Admin,
            cancellationToken);
        await EnsureUserAsync(
            dbContext, hasher, "demo@flashseat.dev", "Demo@123456", "Demo Customer", UserRole.Customer,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureUserAsync(
        IdentityDbContext dbContext,
        IPasswordHasher<User> hasher,
        string email,
        string password,
        string fullName,
        UserRole role,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = email.ToUpperInvariant();
        if (await dbContext.Users.AnyAsync(x => x.Email == normalizedEmail, cancellationToken))
        {
            return;
        }

        var user = new User(Guid.NewGuid(), email, fullName, DateTimeOffset.UtcNow);
        user.SetRole(role);
        user.SetPasswordHash(hasher.HashPassword(user, password));
        dbContext.Users.Add(user);
    }
}
