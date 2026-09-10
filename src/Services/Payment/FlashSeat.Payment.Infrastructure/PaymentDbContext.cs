using FlashSeat.Payment.Domain;
using MassTransit;
using Microsoft.EntityFrameworkCore;
namespace FlashSeat.Payment.Infrastructure;
public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<global::FlashSeat.Payment.Domain.Payment> Payments => Set<global::FlashSeat.Payment.Domain.Payment>();
    public DbSet<PaymentWebhookReceipt> PaymentWebhookReceipts => Set<PaymentWebhookReceipt>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<global::FlashSeat.Payment.Domain.Payment>(e =>
        { e.ToTable("payments"); e.HasKey(x => x.Id); e.HasIndex(x => x.BookingId).IsUnique(); e.HasIndex(x => x.IdempotencyKey).IsUnique(); e.HasIndex(x => x.UserId); e.HasIndex(x => x.OrderCode).HasFilter("\"OrderCode\" <> 0").IsUnique(); e.Property(x => x.OrderCode); e.Property(x => x.PaymentLinkId).HasMaxLength(100); e.Property(x => x.CheckoutUrl).HasMaxLength(2000); e.Property(x => x.QrCode).HasMaxLength(2000); e.Property(x => x.ProviderReference).HasMaxLength(200); e.Property(x => x.ProviderStatus).HasMaxLength(40); e.HasIndex(x => x.PaymentLinkId).IsUnique(); e.HasIndex(x => x.ProviderReference).IsUnique(); e.Property(x => x.Status).HasConversion<string>(); e.Property(x => x.Amount).HasPrecision(18, 2); e.Property(x => x.IdempotencyKey).HasMaxLength(100); e.Property(x => x.RequestFingerprint).HasMaxLength(64); });
        modelBuilder.Entity<PaymentWebhookReceipt>(e =>
        { e.ToTable("payment_webhook_receipts"); e.HasKey(x => x.Id); e.HasIndex(x => x.EventKey).IsUnique(); e.Property(x => x.EventKey).HasMaxLength(128); });
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
