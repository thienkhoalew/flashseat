using System.Globalization;
using System.Net.Http;
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
        if (!includeAll) query = query.Where(x => x.Status == EventStatus.Published);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower(CultureInfo.InvariantCulture);
            query = query.Where(x => x.Name.ToLower().Contains(term) || x.VenueName.ToLower().Contains(term));
        }
        if (from.HasValue) query = query.Where(x => x.StartsAt >= from.Value);
        if (endAt.HasValue) query = query.Where(x => x.StartsAt <= endAt.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        query = sort == "createdAt" ? query.OrderByDescending(x => x.CreatedAt) : query.OrderBy(x => x.StartsAt);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new EventListItem(x.Id, x.Name, x.Slug, x.ImageUrl, x.VenueName, x.StartsAt,
                x.Seats.Select(s => (decimal?)s.Price).Min() ?? 0,
                x.Seats.Select(s => s.Currency).FirstOrDefault() ?? "VND", x.Status.ToString()))
            .ToListAsync(cancellationToken);
        return new PagedResponse<EventListItem>(items, page, pageSize, totalCount);
    }

    public async Task<EventDetailResponse?> GetEventAsync(Guid eventId, bool includeUnpublished, CancellationToken cancellationToken)
    {
        var query = dbContext.Events.AsNoTracking().Where(x => x.Id == eventId);
        if (!includeUnpublished) query = query.Where(x => x.Status == EventStatus.Published);
        return await query.Select(ToDetail()).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<EventDetailResponse?> CreateAsync(SaveEventRequest request, CancellationToken cancellationToken)
    {
        if (await dbContext.Events.AnyAsync(x => x.Slug == request.Slug, cancellationToken)) return null;
        var entity = new EventEntity(Guid.NewGuid(), request.Name, request.Slug, request.Description, request.ImageUrl,
            request.VenueName, request.Address, request.StartsAt, request.SalesStartAt, request.SalesEndAt,
            timeProvider.GetUtcNow());
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
        entity.Update(request.Name, request.Slug, request.Description, request.ImageUrl, request.VenueName,
            request.Address, request.StartsAt, request.SalesStartAt, request.SalesEndAt, timeProvider.GetUtcNow());
        dbContext.Seats.RemoveRange(entity.Seats);
        foreach (var seat in request.Seats)
            entity.Seats.Add(new Seat(Guid.NewGuid(), entity.Id, seat.Section, seat.Row, seat.Number, seat.Price, seat.Currency));
        await dbContext.SaveChangesAsync(cancellationToken);
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

    private static System.Linq.Expressions.Expression<Func<EventEntity, EventDetailResponse>> ToDetail() => x =>
        new EventDetailResponse(x.Id, x.Name, x.Slug, x.Description, x.ImageUrl, x.VenueName, x.Address,
            x.StartsAt, x.SalesStartAt, x.SalesEndAt, x.Status.ToString(),
            x.Seats.OrderBy(s => s.Section).ThenBy(s => s.Row).ThenBy(s => s.Number)
                .Select(s => new SeatResponse(s.Id, s.Section, s.Row, s.Number, s.Price, s.Currency)).ToList());
}
