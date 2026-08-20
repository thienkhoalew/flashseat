using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using FlashSeat.Events.Application;
using FlashSeat.Events.Domain;
using Microsoft.EntityFrameworkCore;

namespace FlashSeat.Events.Infrastructure;

public sealed class EventService(
    EventsDbContext dbContext,
    BookingInventoryClient bookingInventoryClient,
    TimeProvider timeProvider) : IEventService
{
    public async Task<PagedResponse<EventListItem>> GetEventsAsync(
        string? search, DateTimeOffset? from, DateTimeOffset? endAt, int page, int pageSize,
        string sort, bool includeAll, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var query = dbContext.Events.AsNoTracking().Where(x => x.DeletedAt == null).AsQueryable();
        if (!includeAll)
            query = query.Where(x => x.Status == EventStatus.Published && x.EndsAt > now);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower(CultureInfo.InvariantCulture);
            query = query.Where(x => x.Name.ToLower().Contains(term) || x.VenueName.ToLower().Contains(term));
        }
        if (from.HasValue) query = query.Where(x => x.StartsAt >= from.Value);
        if (endAt.HasValue) query = query.Where(x => x.StartsAt <= endAt.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        query = sort == "createdAt" ? query.OrderByDescending(x => x.CreatedAt) : query.OrderBy(x => x.StartsAt);
        var items = await query
            .Select(x => new EventListItem(x.Id, x.Name, x.Slug, x.ImageUrl, x.VenueName, x.StartsAt, x.EndsAt,
                x.SalesStartAt, x.SalesEndAt, x.Seats.Select(s => (decimal?)s.Price).Min() ?? 0,
                x.Seats.Select(s => s.Currency).FirstOrDefault() ?? "VND",
                x.Status == EventStatus.Completed || x.EndsAt <= now
                    ? EventStatus.Ended.ToString() : x.Status.ToString(),
                "Unknown", null, null, null, null, null, null))
            .ToListAsync(cancellationToken);
        var enrichedItems = await EnrichAsync(items, cancellationToken);
        if (!includeAll)
            enrichedItems = enrichedItems.OrderBy(x => PublicListingStatus(x, now)).ThenBy(x => x.StartsAt).ToList();
        var pagedItems = enrichedItems.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new PagedResponse<EventListItem>(pagedItems, page, pageSize, totalCount);
    }

    public async Task<EventDetailResponse?> GetEventAsync(Guid eventId, bool includeUnpublished, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var query = dbContext.Events.AsNoTracking().Where(x => x.Id == eventId && x.DeletedAt == null);
        if (!includeUnpublished)
            query = query.Where(x => x.Status == EventStatus.Published && x.EndsAt > now);
        var detail = await query.Select(ToDetail(now)).SingleOrDefaultAsync(cancellationToken);
        if (detail is null) return null;
        return (await EnrichAsync([detail], cancellationToken)).Single();
    }

    public Task<EventMetadataResponse?> GetMetadataAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        return dbContext.Events.AsNoTracking().Where(x => x.Id == eventId)
            .Select(x => new EventMetadataResponse(x.Id, x.Name, x.Slug, x.Description, x.ImageUrl,
                x.VenueName, x.Address, x.StartsAt, x.EndsAt,
                x.Status == EventStatus.Completed || x.EndsAt <= now
                    ? EventStatus.Ended.ToString() : x.Status.ToString(), x.DeletedAt != null))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<EventDetailResponse?> CreateAsync(SaveEventRequest request, CancellationToken cancellationToken)
    {
        if (await dbContext.Events.AnyAsync(x => x.Slug == request.Slug && x.DeletedAt == null, cancellationToken)) return null;
        var now = timeProvider.GetUtcNow();
        var entity = new EventEntity(Guid.NewGuid(), request.Name, request.Slug, request.Description, request.ImageUrl,
            request.VenueName, request.Address, request.StartsAt, request.EndsAt, request.SalesStartAt,
            request.SalesEndAt, now);
        AddSeats(entity, request.Seats);
        dbContext.Events.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetEventAsync(entity.Id, true, cancellationToken);
    }

    public async Task<EventDetailResponse?> UpdateAsync(Guid eventId, SaveEventRequest request, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Events.Include(x => x.Seats).SingleOrDefaultAsync(x => x.Id == eventId && x.DeletedAt == null, cancellationToken);
        if (entity is null) return null;
        var now = timeProvider.GetUtcNow();
        var activity = await GetActivityOrThrowAsync(eventId, cancellationToken);
        if (activity.HasHistoricalActivity)
            throw new EventLifecycleException("sales_activity_exists", "Events with booking activity cannot be edited.");
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        entity.Update(request.Name, request.Slug, request.Description, request.ImageUrl, request.VenueName,
            request.Address, request.StartsAt, request.EndsAt, request.SalesStartAt, request.SalesEndAt, now);
        dbContext.Seats.RemoveRange(entity.Seats);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        entity = await dbContext.Events.Include(x => x.Seats).SingleAsync(x => x.Id == eventId, cancellationToken);
        AddSeats(entity, request.Seats);
        await dbContext.SaveChangesAsync(cancellationToken);
        try
        {
            await bookingInventoryClient.ReplaceAsync(entity, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new EventLifecycleException("booking_dependency_unavailable", exception.Message, 503);
        }
        return await GetEventAsync(entity.Id, true, cancellationToken);
    }

    public async Task<bool> PublishAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Events.Include(x => x.Seats).SingleOrDefaultAsync(x => x.Id == eventId && x.DeletedAt == null, cancellationToken);
        if (entity is null) return false;
        try
        {
            await bookingInventoryClient.ImportAsync(entity, cancellationToken);
            entity.Publish(timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (HttpRequestException exception)
        {
            throw new EventLifecycleException("booking_dependency_unavailable", exception.Message, 503);
        }
    }

    public async Task<bool> CancelAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Events.SingleOrDefaultAsync(x => x.Id == eventId && x.DeletedAt == null, cancellationToken);
        if (entity is null) return false;
        entity.Cancel(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UnpublishAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Events.SingleOrDefaultAsync(x => x.Id == eventId && x.DeletedAt == null, cancellationToken);
        if (entity is null) return false;
        await EnsureNoActivityAsync(eventId, cancellationToken);
        entity.Unpublish(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RestoreDraftAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Events.SingleOrDefaultAsync(x => x.Id == eventId && x.DeletedAt == null, cancellationToken);
        if (entity is null) return false;
        await EnsureNoActivityAsync(eventId, cancellationToken);
        entity.RestoreDraft(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RepublishAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Events.Include(x => x.Seats).SingleOrDefaultAsync(x => x.Id == eventId && x.DeletedAt == null, cancellationToken);
        if (entity is null) return false;
        try
        {
            await bookingInventoryClient.ImportAsync(entity, cancellationToken);
            entity.Republish(timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (HttpRequestException exception)
        {
            throw new EventLifecycleException("booking_dependency_unavailable", exception.Message, 503);
        }
    }

    public async Task<bool> ArchiveAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Events.SingleOrDefaultAsync(x => x.Id == eventId && x.DeletedAt == null, cancellationToken);
        if (entity is null) return false;
        var now = timeProvider.GetUtcNow();
        if (entity.EffectiveStatus(now) == EventStatus.Draft)
            await EnsureNoActivityAsync(eventId, cancellationToken);
        entity.Archive(now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<EventActivity> GetActivityOrThrowAsync(Guid eventId, CancellationToken cancellationToken)
    {
        try { return await bookingInventoryClient.GetActivityAsync(eventId, cancellationToken); }
        catch (HttpRequestException exception)
        {
            throw new EventLifecycleException("booking_dependency_unavailable", exception.Message, 503);
        }
    }

    private async Task EnsureNoActivityAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var activity = await GetActivityOrThrowAsync(eventId, cancellationToken);
        if (activity.HasHistoricalActivity)
            throw new EventLifecycleException("sales_activity_exists", "This event has booking activity and cannot be changed.");
    }

    private static void AddSeats(EventEntity entity, IEnumerable<SeatInput> seats)
    {
        foreach (var seat in seats)
            entity.Seats.Add(new Seat(Guid.NewGuid(), entity.Id, seat.Section, seat.Row, seat.Number, seat.Price, seat.Currency));
    }

    private async Task<IReadOnlyCollection<EventListItem>> EnrichAsync(IReadOnlyCollection<EventListItem> items, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<InventorySummary> summaries;
        try { summaries = await bookingInventoryClient.GetSummariesAsync(items.Select(x => x.Id).ToArray(), cancellationToken); }
        catch (Exception exception) when (exception is HttpRequestException or JsonException) { summaries = []; }
        var byEvent = summaries.ToDictionary(x => x.EventId);
        return items.Select(item => byEvent.TryGetValue(item.Id, out var summary)
            ? item with { AvailabilityStatus = summary.AvailabilityStatus, TotalSeatCount = summary.TotalSeatCount, AvailableSeatCount = summary.AvailableSeatCount, HeldSeatCount = summary.HeldSeatCount, BookedSeatCount = summary.BookedSeatCount, InventoryVersion = summary.InventoryVersion, AvailabilityAsOf = summary.AvailabilityAsOf }
            : item).ToList();
    }

    private async Task<IReadOnlyCollection<EventDetailResponse>> EnrichAsync(IReadOnlyCollection<EventDetailResponse> items, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<InventorySummary> summaries;
        try { summaries = await bookingInventoryClient.GetSummariesAsync(items.Select(x => x.Id).ToArray(), cancellationToken); }
        catch (Exception exception) when (exception is HttpRequestException or JsonException) { summaries = []; }
        var byEvent = summaries.ToDictionary(x => x.EventId);
        return items.Select(item => byEvent.TryGetValue(item.Id, out var summary)
            ? item with { AvailabilityStatus = summary.AvailabilityStatus, TotalSeatCount = summary.TotalSeatCount, AvailableSeatCount = summary.AvailableSeatCount, HeldSeatCount = summary.HeldSeatCount, BookedSeatCount = summary.BookedSeatCount, InventoryVersion = summary.InventoryVersion, AvailabilityAsOf = summary.AvailabilityAsOf }
            : item).ToList();
    }

    private static int PublicListingStatus(EventListItem item, DateTimeOffset now)
    {
        var salesAreOpen = item.SalesStartAt <= now && now < item.SalesEndAt;
        if (salesAreOpen) return item.AvailabilityStatus == "SoldOut" ? 1 : 0;
        if (now < item.SalesStartAt) return 2;
        return 3;
    }

    private static string EffectiveStatus(EventStatus status, DateTimeOffset endsAt, DateTimeOffset now) =>
        status == EventStatus.Completed || endsAt <= now
            ? EventStatus.Ended.ToString()
            : status.ToString();

    private static System.Linq.Expressions.Expression<Func<EventEntity, EventDetailResponse>> ToDetail(DateTimeOffset now) => x =>
        new EventDetailResponse(x.Id, x.Name, x.Slug, x.Description, x.ImageUrl, x.VenueName, x.Address,
            x.StartsAt, x.EndsAt, x.SalesStartAt, x.SalesEndAt,
            x.Status == EventStatus.Completed || x.EndsAt <= now
                ? EventStatus.Ended.ToString() : x.Status.ToString(),
            x.Seats.OrderBy(s => s.Section).ThenBy(s => s.Row).ThenBy(s => s.Number)
                .Select(s => new SeatResponse(s.Id, s.Section, s.Row, s.Number, s.Price, s.Currency)).ToList(),
            "Unknown", null, null, null, null, null, null);
}
