using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using FlashSeat.Events.Application;
using FlashSeat.Events.Domain;
using Microsoft.EntityFrameworkCore;

namespace FlashSeat.Events.Infrastructure;

public sealed class EventService(EventsDbContext dbContext, BookingInventoryClient bookingInventoryClient,
    TimeProvider timeProvider) : IEventService
{
    public async Task<PagedResponse<EventListItem>> GetEventsAsync(string? search, DateTimeOffset? from,
        DateTimeOffset? endAt, int page, int pageSize, string sort, bool includeAll, CancellationToken cancellationToken)
    {
        var query = dbContext.Events.AsNoTracking().AsQueryable();
        if (!includeAll)
        {
            var now = timeProvider.GetUtcNow();
            query = query.Where(x => x.Status == EventStatus.Published && x.EndsAt > now);
        }
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
                x.Seats.Select(s => s.Currency).FirstOrDefault() ?? "VND", x.Status.ToString(), "Unknown", null, null, null, null, null, null))
            .ToListAsync(cancellationToken);
        var enrichedItems = await EnrichAsync(items, cancellationToken);

        if (!includeAll)
        {
            var now = timeProvider.GetUtcNow();
            enrichedItems = enrichedItems
                .OrderBy(x => PublicListingStatus(x, now))
                .ThenBy(x => x.StartsAt)
                .ToList();
        }

        var pagedItems = enrichedItems
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
        return new PagedResponse<EventListItem>(pagedItems, page, pageSize, totalCount);
    }

    public async Task<EventDetailResponse?> GetEventAsync(Guid eventId, bool includeUnpublished, CancellationToken cancellationToken)
    {
        var query = dbContext.Events.AsNoTracking().Where(x => x.Id == eventId);
        if (!includeUnpublished) query = query.Where(x => x.Status == EventStatus.Published);
        var detail = await query.Select(ToDetail()).SingleOrDefaultAsync(cancellationToken);
        if (detail is null) return null;
        return (await EnrichAsync([detail], cancellationToken)).Single();
    }

    public async Task<EventDetailResponse?> CreateAsync(SaveEventRequest request, CancellationToken cancellationToken)
    {
        if (await dbContext.Events.AnyAsync(x => x.Slug == request.Slug, cancellationToken)) return null;
        var entity = new EventEntity(Guid.NewGuid(), request.Name, request.Slug, request.Description, request.ImageUrl,
            request.VenueName, request.Address, request.StartsAt, request.EndsAt, request.SalesStartAt,
            request.SalesEndAt, timeProvider.GetUtcNow());
        foreach (var seat in request.Seats)
            entity.Seats.Add(new Seat(Guid.NewGuid(), entity.Id, seat.Section, seat.Row, seat.Number, seat.Price, seat.Currency));
        dbContext.Events.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetEventAsync(entity.Id, true, cancellationToken);
    }

    public async Task<EventDetailResponse?> UpdateAsync(Guid eventId, SaveEventRequest request, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Events.Include(x => x.Seats).SingleOrDefaultAsync(x => x.Id == eventId, cancellationToken);
        if (entity is null) return null;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        entity.Update(request.Name, request.Slug, request.Description, request.ImageUrl, request.VenueName,
            request.Address, request.StartsAt, request.EndsAt, request.SalesStartAt, request.SalesEndAt, timeProvider.GetUtcNow());
        dbContext.Seats.RemoveRange(entity.Seats);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        foreach (var seat in request.Seats)
            dbContext.Seats.Add(new Seat(Guid.NewGuid(), eventId, seat.Section, seat.Row, seat.Number, seat.Price, seat.Currency));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetEventAsync(entity.Id, true, cancellationToken);
    }

    public async Task<bool> PublishAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Events.Include(x => x.Seats).SingleOrDefaultAsync(x => x.Id == eventId, cancellationToken);
        if (entity is null) return false;
        entity.Publish(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
        try
        {
            await bookingInventoryClient.ImportAsync(entity, cancellationToken);
            return true;
        }
        catch (HttpRequestException)
        {
            entity.Cancel(timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task<bool> CancelAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Events.SingleOrDefaultAsync(x => x.Id == eventId, cancellationToken);
        if (entity is null) return false;
        entity.Cancel(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
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

    private static System.Linq.Expressions.Expression<Func<EventEntity, EventDetailResponse>> ToDetail() => x =>
        new EventDetailResponse(x.Id, x.Name, x.Slug, x.Description, x.ImageUrl, x.VenueName, x.Address,
            x.StartsAt, x.EndsAt, x.SalesStartAt, x.SalesEndAt, x.Status.ToString(),
            x.Seats.OrderBy(s => s.Section).ThenBy(s => s.Row).ThenBy(s => s.Number)
                .Select(s => new SeatResponse(s.Id, s.Section, s.Row, s.Number, s.Price, s.Currency)).ToList(), "Unknown", null, null, null, null, null, null);
}
