namespace FlashSeat.Booking.Domain;

public enum SeatInventoryStatus { Available, Held, Booked }
public enum SeatHoldStatus { Active, Converted, Expired, Released }
public enum BookingStatus { PendingPayment, Confirmed, Cancelled, Expired }

public sealed class EventSeatInventory
{
    private EventSeatInventory() { }
    public EventSeatInventory(Guid id, Guid eventId, Guid seatId, string section, string row, int number, decimal price, string currency)
    { Id = id; EventId = eventId; SeatId = seatId; Section = section; Row = row; Number = number; Price = price; Currency = currency; }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid SeatId { get; private set; }
    public string Section { get; private set; } = string.Empty;
    public string Row { get; private set; } = string.Empty;
    public int Number { get; private set; }
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = "VND";
    public SeatInventoryStatus Status { get; private set; }
    public Guid? HoldId { get; private set; }
    public DateTimeOffset? HoldExpiresAt { get; private set; }
    public Guid? BookingId { get; private set; }
    public void Hold(Guid holdId, DateTimeOffset expiresAt) { if (Status != SeatInventoryStatus.Available) throw new InvalidOperationException(); Status = SeatInventoryStatus.Held; HoldId = holdId; HoldExpiresAt = expiresAt; }
    public void AssignBooking(Guid holdId, Guid bookingId) { if (Status != SeatInventoryStatus.Held || HoldId != holdId) throw new InvalidOperationException(); BookingId = bookingId; }
    public void Book(Guid bookingId) { if (Status != SeatInventoryStatus.Held || BookingId != bookingId) return; Status = SeatInventoryStatus.Booked; HoldId = null; HoldExpiresAt = null; }
    public void Release(Guid holdId, Guid? bookingId = null) { if (Status == SeatInventoryStatus.Held && HoldId == holdId && BookingId == bookingId) { Status = SeatInventoryStatus.Available; HoldId = null; HoldExpiresAt = null; BookingId = null; } }
}

public sealed class SeatHold
{
    private SeatHold() { }
    public SeatHold(Guid id, Guid userId, Guid eventId, DateTimeOffset expiresAt, DateTimeOffset createdAt)
    { Id = id; UserId = userId; EventId = eventId; ExpiresAt = expiresAt; CreatedAt = createdAt; }
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid EventId { get; private set; }
    public SeatHoldStatus Status { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public ICollection<SeatHoldItem> Items { get; } = [];
    public void Convert() { if (Status != SeatHoldStatus.Active) throw new InvalidOperationException(); Status = SeatHoldStatus.Converted; }
    public void Expire() { if (Status is SeatHoldStatus.Active or SeatHoldStatus.Converted) Status = SeatHoldStatus.Expired; }
    public void Release() { if (Status == SeatHoldStatus.Active) Status = SeatHoldStatus.Released; }
}

public sealed class SeatHoldItem
{
    private SeatHoldItem() { }
    public SeatHoldItem(Guid holdId, Guid seatInventoryId, decimal price) { HoldId = holdId; SeatInventoryId = seatInventoryId; Price = price; }
    public Guid HoldId { get; private set; }
    public Guid SeatInventoryId { get; private set; }
    public decimal Price { get; private set; }
    public SeatHold Hold { get; private set; } = null!;
}

public sealed class Booking
{
    private Booking() { }
    public Booking(Guid id, string number, Guid userId, Guid eventId, Guid holdId, decimal total, string currency, DateTimeOffset createdAt)
    { Id = id; BookingNumber = number; UserId = userId; EventId = eventId; HoldId = holdId; TotalAmount = total; Currency = currency; CreatedAt = createdAt; }
    public Guid Id { get; private set; }
    public string BookingNumber { get; private set; } = string.Empty;
    public Guid UserId { get; private set; }
    public Guid EventId { get; private set; }
    public Guid HoldId { get; private set; }
    public decimal TotalAmount { get; private set; }
    public string Currency { get; private set; } = "VND";
    public BookingStatus Status { get; private set; }
    public Guid? PaymentId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public ICollection<BookingItem> Items { get; } = [];
    public void Confirm(Guid paymentId, DateTimeOffset now) { if (Status != BookingStatus.PendingPayment) return; Status = BookingStatus.Confirmed; PaymentId = paymentId; ConfirmedAt = now; }
    public void Cancel() { if (Status == BookingStatus.PendingPayment) Status = BookingStatus.Cancelled; }
    public void Expire() { if (Status == BookingStatus.PendingPayment) Status = BookingStatus.Expired; }
}

public sealed class BookingItem
{
    private BookingItem() { }
    public BookingItem(Guid id, Guid bookingId, Guid seatId, string section, string row, int number, decimal price)
    { Id = id; BookingId = bookingId; SeatId = seatId; Section = section; Row = row; Number = number; Price = price; }
    public Guid Id { get; private set; }
    public Guid BookingId { get; private set; }
    public Guid SeatId { get; private set; }
    public string Section { get; private set; } = string.Empty;
    public string Row { get; private set; } = string.Empty;
    public int Number { get; private set; }
    public decimal Price { get; private set; }
    public Booking Booking { get; private set; } = null!;
}
