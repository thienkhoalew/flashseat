using FlashSeat.Payment.Domain;
using MassTransit;
using Microsoft.EntityFrameworkCore;
namespace FlashSeat.Payment.Infrastructure;
public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<global::FlashSeat.Payment.Domain.Payment> Payments => Set<global::FlashSeat.Payment.Domain.Payment>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<global::FlashSeat.Payment.Domain.Payment>(e =>
        { e.ToTable("payments"); e.HasKey(x => x.Id); e.HasIndex(x => x.BookingId).IsUnique(); e.HasIndex(x => x.IdempotencyKey).IsUnique(); e.HasIndex(x => x.UserId); e.Property(x => x.Status).HasConversion<string>(); e.Property(x => x.Amount).HasPrecision(18, 2); e.Property(x => x.IdempotencyKey).HasMaxLength(100); e.Property(x => x.RequestFingerprint).HasMaxLength(64); });
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
