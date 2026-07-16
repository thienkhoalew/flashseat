using FlashSeat.Events.Domain;
using Microsoft.EntityFrameworkCore;

namespace FlashSeat.Events.Infrastructure;

public sealed class EventsDbContext(DbContextOptions<EventsDbContext> options) : DbContext(options)
{
    public DbSet<EventEntity> Events => Set<EventEntity>();
    public DbSet<Seat> Seats => Set<Seat>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EventEntity>(entity =>
        {
            entity.ToTable("events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(150);
            entity.Property(x => x.Slug).HasMaxLength(160);
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.Property(x => x.Description).HasMaxLength(5000);
            entity.Property(x => x.ImageUrl).HasMaxLength(2048);
            entity.Property(x => x.VenueName).HasMaxLength(200);
            entity.Property(x => x.Address).HasMaxLength(500);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property<uint>("xmin").IsRowVersion();
            entity.HasMany(x => x.Seats).WithOne(x => x.Event).HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Seat>(entity =>
        {
            entity.ToTable("seats");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Section).HasMaxLength(50);
            entity.Property(x => x.Row).HasMaxLength(10);
            entity.Property(x => x.Price).HasPrecision(18, 2);
            entity.Property(x => x.Currency).HasMaxLength(3);
            entity.HasIndex(x => new { x.EventId, x.Section, x.Row, x.Number }).IsUnique();
        });
    }
}
