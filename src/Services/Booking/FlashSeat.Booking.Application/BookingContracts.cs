namespace FlashSeat.Booking.Application;

public sealed record CreateHoldRequest(Guid EventId, IReadOnlyCollection<Guid> SeatIds);
public sealed record CreateBookingRequest(Guid HoldId);
public sealed record InventoryImportRequest(Guid EventId, IReadOnlyCollection<InventorySeatInput> Seats);
public sealed record InventoryReplacementRequest(Guid EventId, IReadOnlyCollection<InventorySeatInput> Seats);
public sealed record InventorySummaryRequest(IReadOnlyCollection<Guid> EventIds);
public sealed record EventActivityResponse(Guid EventId, bool HasHistoricalActivity, int ActiveHoldCount, int PendingBookingCount);
public sealed record InventorySeatInput(Guid SeatId, string Section, string Row, int Number, decimal Price, string Currency);
public sealed record SeatAvailabilityResponse(Guid SeatId, string Status, DateTimeOffset? HoldExpiresAt);
public sealed record EventInventorySummaryResponse(Guid EventId, int TotalSeatCount, int AvailableSeatCount, int HeldSeatCount, int BookedSeatCount, long InventoryVersion, DateTimeOffset AvailabilityAsOf);
public sealed record HoldItemResponse(Guid SeatId, string Section, string Row, int Number, decimal Price);
public sealed record HoldResponse(Guid Id, Guid EventId, string Status, DateTimeOffset ExpiresAt,
    IReadOnlyCollection<HoldItemResponse> Items, decimal TotalAmount, string Currency);
public sealed record BookingItemResponse(Guid Id, Guid SeatId, string Section, string Row, int Number, decimal Price,
    string Currency, string TicketCode, string CheckInStatus, DateTimeOffset? CheckedInAt, Guid? CheckedInBy = null);
public sealed record BookingEventResponse(Guid Id, string Name, string Slug, string Description, string ImageUrl,
    string VenueName, string Address, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Status);
public sealed record BookingResponse(Guid Id, string BookingNumber, Guid EventId, string Status,
    decimal TotalAmount, string Currency, DateTimeOffset CreatedAt, DateTimeOffset? ConfirmedAt,
    BookingEventResponse? Event, IReadOnlyCollection<BookingItemResponse> Items);
public sealed record EventMetadataResponse(Guid Id, string Name, string Slug, string Description, string ImageUrl,
    string VenueName, string Address, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Status, bool IsArchived = false);
public sealed record CheckInRequest(string TicketCode);
public sealed record CheckInResponse(string TicketCode, string Status, DateTimeOffset? CheckedInAt,
    string BookingNumber, BookingEventResponse? Event, BookingItemResponse Ticket);
public enum CheckInFailure { UnknownTicket, BookingNotConfirmed, AlreadyCheckedIn }
public sealed record CheckInAttemptResult(CheckInResponse? Response, CheckInFailure? Failure = null);
public enum HoldAttemptFailure { SeatsUnavailable, ActiveHoldExists, LockContention, SalesNotOpen, SalesWindowUnavailable }
public sealed record HoldAttemptResult(HoldResponse? Hold, IReadOnlyCollection<Guid> UnavailableSeatIds, HoldAttemptFailure? Failure = null);

public interface IBookingService
{
    Task<IReadOnlyCollection<SeatAvailabilityResponse>> GetAvailabilityAsync(Guid eventId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<EventInventorySummaryResponse>> GetInventorySummariesAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken);
    Task<EventActivityResponse> GetEventActivityAsync(Guid eventId, CancellationToken cancellationToken);
    Task ReplaceInventoryAsync(InventoryReplacementRequest request, CancellationToken cancellationToken);
    Task<HoldAttemptResult> CreateHoldAsync(Guid userId, CreateHoldRequest request, CancellationToken cancellationToken);
    Task<HoldResponse?> GetHoldAsync(Guid userId, Guid holdId, CancellationToken cancellationToken);
    Task<bool> ReleaseHoldAsync(Guid userId, Guid holdId, CancellationToken cancellationToken);
    Task<BookingResponse?> CreateBookingAsync(Guid userId, CreateBookingRequest request, CancellationToken cancellationToken);
    Task<BookingResponse?> GetBookingAsync(Guid userId, bool isAdmin, Guid bookingId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<BookingResponse>> GetBookingsAsync(Guid userId, CancellationToken cancellationToken);
    Task<CheckInAttemptResult> CheckInAsync(Guid operatorId, string ticketCode, CancellationToken cancellationToken);
    Task ImportInventoryAsync(InventoryImportRequest request, CancellationToken cancellationToken);
}
