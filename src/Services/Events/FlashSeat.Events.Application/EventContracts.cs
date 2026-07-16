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
    DateTimeOffset SalesStartAt,
    DateTimeOffset SalesEndAt,
    IReadOnlyCollection<SeatInput> Seats);

public sealed record EventListItem(
    Guid Id, string Name, string Slug, string ImageUrl, string VenueName,
    DateTimeOffset StartsAt, decimal MinPrice, string Currency, string Status);

public sealed record SeatResponse(Guid Id, string Section, string Row, int Number, decimal Price, string Currency);

public sealed record EventDetailResponse(
    Guid Id, string Name, string Slug, string Description, string ImageUrl, string VenueName,
    string Address, DateTimeOffset StartsAt, DateTimeOffset SalesStartAt, DateTimeOffset SalesEndAt,
    string Status, IReadOnlyCollection<SeatResponse> Seats);

public sealed record PagedResponse<T>(IReadOnlyCollection<T> Items, int Page, int PageSize, int TotalCount);
