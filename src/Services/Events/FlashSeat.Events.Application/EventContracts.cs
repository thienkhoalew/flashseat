namespace FlashSeat.Events.Application;

public sealed record SeatInput(string Section, string Row, int Number, decimal Price, string Currency = "VND");

public sealed record SaveEventRequest(
    string Name,
    string Slug,
    string Description,
    string ImageUrl,
    string VenueName,
    string Address,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateTimeOffset SalesStartAt,
    DateTimeOffset SalesEndAt,
    IReadOnlyCollection<SeatInput> Seats);

public sealed record EventListItem(
    Guid Id, string Name, string Slug, string ImageUrl, string VenueName,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, DateTimeOffset SalesStartAt, DateTimeOffset SalesEndAt,
    decimal MinPrice, string Currency, string Status, string AvailabilityStatus = "Unknown",
    int? TotalSeatCount = null, int? AvailableSeatCount = null, int? HeldSeatCount = null,
    int? BookedSeatCount = null, long? InventoryVersion = null, DateTimeOffset? AvailabilityAsOf = null);

public sealed record SeatResponse(Guid Id, string Section, string Row, int Number, decimal Price, string Currency);

public sealed record EventDetailResponse(
    Guid Id, string Name, string Slug, string Description, string ImageUrl, string VenueName,
    string Address, DateTimeOffset StartsAt, DateTimeOffset EndsAt, DateTimeOffset SalesStartAt, DateTimeOffset SalesEndAt,
    string Status, IReadOnlyCollection<SeatResponse> Seats, string AvailabilityStatus = "Unknown",
    int? TotalSeatCount = null, int? AvailableSeatCount = null, int? HeldSeatCount = null,
    int? BookedSeatCount = null, long? InventoryVersion = null, DateTimeOffset? AvailabilityAsOf = null);

public sealed record PagedResponse<T>(IReadOnlyCollection<T> Items, int Page, int PageSize, int TotalCount);

public sealed record EventMetadataResponse(Guid Id, string Name, string Slug, string Description, string ImageUrl,
    string VenueName, string Address, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Status, bool IsArchived = false);

public sealed class EventLifecycleException(string code, string message, int statusCode = 409) : InvalidOperationException(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}
