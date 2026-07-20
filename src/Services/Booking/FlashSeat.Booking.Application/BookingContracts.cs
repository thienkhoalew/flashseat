namespace FlashSeat.Booking.Application;

public sealed record CreateHoldRequest(Guid EventId, IReadOnlyCollection<Guid> SeatIds);
public sealed record CreateBookingRequest(Guid HoldId);
public sealed record InventoryImportRequest(Guid EventId, IReadOnlyCollection<InventorySeatInput> Seats);
public sealed record InventorySeatInput(Guid SeatId, string Section, string Row, int Number, decimal Price, string Currency);
public sealed record SeatAvailabilityResponse(Guid SeatId, string Status, DateTimeOffset? HoldExpiresAt);
public sealed record HoldItemResponse(Guid SeatId, string Section, string Row, int Number, decimal Price);
public sealed record HoldResponse(Guid Id, Guid EventId, string Status, DateTimeOffset ExpiresAt,
    IReadOnlyCollection<HoldItemResponse> Items, decimal TotalAmount, string Currency);
public sealed record BookingItemResponse(Guid SeatId, string Section, string Row, int Number, decimal Price);
public sealed record BookingResponse(Guid Id, string BookingNumber, Guid EventId, string Status,
    decimal TotalAmount, string Currency, DateTimeOffset CreatedAt, IReadOnlyCollection<BookingItemResponse> Items);
public enum HoldAttemptFailure { SeatsUnavailable, ActiveHoldExists, LockContention }
public sealed record HoldAttemptResult(HoldResponse? Hold, IReadOnlyCollection<Guid> UnavailableSeatIds, HoldAttemptFailure? Failure = null);

public interface IBookingService
{
    Task<IReadOnlyCollection<SeatAvailabilityResponse>> GetAvailabilityAsync(Guid eventId, CancellationToken cancellationToken);
    Task<HoldAttemptResult> CreateHoldAsync(Guid userId, CreateHoldRequest request, CancellationToken cancellationToken);
    Task<HoldResponse?> GetHoldAsync(Guid userId, Guid holdId, CancellationToken cancellationToken);
    Task<bool> ReleaseHoldAsync(Guid userId, Guid holdId, CancellationToken cancellationToken);
    Task<BookingResponse?> CreateBookingAsync(Guid userId, CreateBookingRequest request, CancellationToken cancellationToken);
    Task<BookingResponse?> GetBookingAsync(Guid userId, bool isAdmin, Guid bookingId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<BookingResponse>> GetBookingsAsync(Guid userId, CancellationToken cancellationToken);
    Task ImportInventoryAsync(InventoryImportRequest request, CancellationToken cancellationToken);
}
