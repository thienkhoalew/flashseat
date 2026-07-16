namespace FlashSeat.Events.Application;

public interface IEventService
{
    Task<PagedResponse<EventListItem>> GetEventsAsync(string? search, DateTimeOffset? from, DateTimeOffset? endAt,
        int page, int pageSize, string sort, bool includeAll, CancellationToken cancellationToken);
    Task<EventDetailResponse?> GetEventAsync(Guid eventId, bool includeUnpublished, CancellationToken cancellationToken);
    Task<EventDetailResponse?> CreateAsync(SaveEventRequest request, CancellationToken cancellationToken);
    Task<EventDetailResponse?> UpdateAsync(Guid eventId, SaveEventRequest request, CancellationToken cancellationToken);
    Task<bool> PublishAsync(Guid eventId, CancellationToken cancellationToken);
    Task<bool> CancelAsync(Guid eventId, CancellationToken cancellationToken);
}
