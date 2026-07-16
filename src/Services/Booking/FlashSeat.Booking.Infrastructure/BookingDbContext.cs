using FlashSeat.Booking.Domain;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace FlashSeat.Booking.Infrastructure;

public sealed class BookingDbContext(DbContextOptions<BookingDbContext> options) : DbContext(options)
{
    public DbSet<EventSeatInventory> Inventory => Set<EventSeatInventory>();
    public DbSet<SeatHold> Holds => Set<SeatHold>();
    public DbSet<SeatHoldItem> HoldItems => Set<SeatHoldItem>();
    public DbSet<global::FlashSeat.Booking.Domain.Booking> Bookings => Set<global::FlashSeat.Booking.Domain.Booking>();
    public DbSet<BookingItem> BookingItems => Set<BookingItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EventSeatInventory>(e => { e.ToTable("event_seat_inventory"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.EventId, x.SeatId }).IsUnique(); e.Property(x => x.Status).HasConversion<string>(); e.Property(x => x.Price).HasPrecision(18, 2); e.Property<uint>("xmin").IsRowVersion(); });
        modelBuilder.Entity<SeatHold>(e => { e.ToTable("seat_holds"); e.HasKey(x => x.Id); e.Property(x => x.Status).HasConversion<string>(); e.HasIndex(x => new { x.UserId, x.EventId }); e.HasMany(x => x.Items).WithOne(x => x.Hold).HasForeignKey(x => x.HoldId); });
        modelBuilder.Entity<SeatHoldItem>(e => { e.ToTable("seat_hold_items"); e.HasKey(x => new { x.HoldId, x.SeatInventoryId }); e.Property(x => x.Price).HasPrecision(18, 2); });
        modelBuilder.Entity<global::FlashSeat.Booking.Domain.Booking>(e => { e.ToTable("bookings"); e.HasKey(x => x.Id); e.HasIndex(x => x.BookingNumber).IsUnique(); e.HasIndex(x => x.HoldId).IsUnique(); e.HasIndex(x => x.UserId); e.Property(x => x.Status).HasConversion<string>(); e.Property(x => x.TotalAmount).HasPrecision(18, 2); e.HasMany(x => x.Items).WithOne(x => x.Booking).HasForeignKey(x => x.BookingId); });
        modelBuilder.Entity<BookingItem>(e => { e.ToTable("booking_items"); e.HasKey(x => x.Id); e.Property(x => x.Price).HasPrecision(18, 2); });
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
