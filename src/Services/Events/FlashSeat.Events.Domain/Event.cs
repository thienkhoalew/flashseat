namespace FlashSeat.Events.Domain;

public sealed class EventEntity
{
    private EventEntity() { }

    public EventEntity(Guid id, string name, string slug, string description, string imageUrl, string venueName,
        string address, DateTimeOffset startsAt, DateTimeOffset salesStartAt, DateTimeOffset salesEndAt,
        DateTimeOffset now)
    {
        Id = id;
        CreatedAt = now;
        Update(name, slug, description, imageUrl, venueName, address, startsAt, salesStartAt, salesEndAt, now);
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string ImageUrl { get; private set; } = string.Empty;
    public string VenueName { get; private set; } = string.Empty;
    public string Address { get; private set; } = string.Empty;
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset SalesStartAt { get; private set; }
    public DateTimeOffset SalesEndAt { get; private set; }
    public EventStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public ICollection<Seat> Seats { get; } = [];

    public void Update(string name, string slug, string description, string imageUrl, string venueName,
        string address, DateTimeOffset startsAt, DateTimeOffset salesStartAt, DateTimeOffset salesEndAt,
        DateTimeOffset now)
    {
        if (Status != EventStatus.Draft) throw new InvalidOperationException("Only draft events can be updated.");
        Name = name.Trim();
        Slug = slug.Trim().ToLowerInvariant();
        Description = description.Trim();
        ImageUrl = imageUrl.Trim();
        VenueName = venueName.Trim();
        Address = address.Trim();
        StartsAt = startsAt;
        SalesStartAt = salesStartAt;
        SalesEndAt = salesEndAt;
        UpdatedAt = now;
    }

    public void Publish(DateTimeOffset now)
    {
        if (Status != EventStatus.Draft) throw new InvalidOperationException("Only draft events can be published.");
        if (StartsAt <= now || SalesStartAt >= SalesEndAt || SalesEndAt > StartsAt || Seats.Count == 0)
            throw new InvalidOperationException("Event schedule or seats are invalid for publishing.");
        Status = EventStatus.Published;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status is EventStatus.Cancelled or EventStatus.Completed)
            throw new InvalidOperationException("Event cannot be cancelled.");
        Status = EventStatus.Cancelled;
        UpdatedAt = now;
    }
}
