using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlashSeat.Booking.Infrastructure;

public static class BookingDatabaseInitializer
{
    public static async Task InitializeBookingDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        await db.Database.EnsureCreatedAsync(cancellationToken);
    }
}
