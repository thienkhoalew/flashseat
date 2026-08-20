namespace FlashSeat.Events.Domain;

public sealed class EventEntity
{
    private EventEntity() { }

    public EventEntity(Guid id, string name, string slug, string description, string imageUrl, string venueName,
        string address, DateTimeOffset startsAt, DateTimeOffset endsAt, DateTimeOffset salesStartAt,
        DateTimeOffset salesEndAt, DateTimeOffset now)
    {
        Id = id;
        CreatedAt = now;
        SetDetails(name, slug, description, imageUrl, venueName, address, startsAt, endsAt, salesStartAt, salesEndAt, now);
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string ImageUrl { get; private set; } = string.Empty;
    public string VenueName { get; private set; } = string.Empty;
    public string Address { get; private set; } = string.Empty;
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public DateTimeOffset SalesStartAt { get; private set; }
    public DateTimeOffset SalesEndAt { get; private set; }
    public EventStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public ICollection<Seat> Seats { get; } = [];

    public EventStatus EffectiveStatus(DateTimeOffset now) =>
        Status == EventStatus.Completed || EndsAt <= now
            ? EventStatus.Ended
            : Status;

    public void Update(string name, string slug, string description, string imageUrl, string venueName,
        string address, DateTimeOffset startsAt, DateTimeOffset endsAt, DateTimeOffset salesStartAt,
        DateTimeOffset salesEndAt, DateTimeOffset now)
    {
        EnsureMutable(now);
        if (Status != EventStatus.Draft) throw new InvalidOperationException("Only draft events can be updated.");
        SetDetails(name, slug, description, imageUrl, venueName, address, startsAt, endsAt, salesStartAt, salesEndAt, now);
    }

    private void SetDetails(string name, string slug, string description, string imageUrl, string venueName,
        string address, DateTimeOffset startsAt, DateTimeOffset endsAt, DateTimeOffset salesStartAt,
        DateTimeOffset salesEndAt, DateTimeOffset now)
    {
        Name = name.Trim();
        Slug = slug.Trim().ToLowerInvariant();
        Description = description.Trim();
        ImageUrl = imageUrl.Trim();
        VenueName = venueName.Trim();
        Address = address.Trim();
        StartsAt = startsAt;
        EndsAt = endsAt;
        SalesStartAt = salesStartAt;
        SalesEndAt = salesEndAt;
        UpdatedAt = now;
    }

    public void Publish(DateTimeOffset now)
    {
        EnsureMutable(now);
        if (Status != EventStatus.Draft) throw new InvalidOperationException("Only draft events can be published.");
        ValidatePublishSchedule(now);
        Status = EventStatus.Published;
        UpdatedAt = now;
    }

    public void Unpublish(DateTimeOffset now)
    {
        EnsureMutable(now);
        if (Status != EventStatus.Published) throw new InvalidOperationException("Only published events can be returned to draft.");
        if (now >= SalesStartAt) throw new InvalidOperationException("Events cannot be unpublished after sales start.");
        Status = EventStatus.Draft;
        UpdatedAt = now;
    }

    public void RestoreDraft(DateTimeOffset now)
    {
        EnsureMutable(now);
        if (Status != EventStatus.Cancelled) throw new InvalidOperationException("Only cancelled events can be restored.");
        if (now >= SalesStartAt) throw new InvalidOperationException("Events cannot be restored after sales start.");
        Status = EventStatus.Draft;
        UpdatedAt = now;
    }

    public void Republish(DateTimeOffset now)
    {
        EnsureMutable(now);
        if (Status != EventStatus.Cancelled) throw new InvalidOperationException("Only cancelled events can be republished.");
        ValidatePublishSchedule(now);
        Status = EventStatus.Published;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        EnsureMutable(now);
        if (Status is EventStatus.Cancelled or EventStatus.Completed)
            throw new InvalidOperationException("Event cannot be cancelled.");
        Status = EventStatus.Cancelled;
        UpdatedAt = now;
    }

    public void Archive(DateTimeOffset now)
    {
        if (DeletedAt is not null)
            throw new InvalidOperationException("Event is already archived.");

        if (EffectiveStatus(now) is not (EventStatus.Draft or EventStatus.Cancelled or EventStatus.Ended))
            throw new InvalidOperationException("Only draft, cancelled, or ended events can be archived.");

        DeletedAt = now;
        UpdatedAt = now;
    }

    private void EnsureMutable(DateTimeOffset now)
    {
        if (DeletedAt is not null || EndsAt <= now || EffectiveStatus(now) == EventStatus.Ended)
            throw new InvalidOperationException("Ended events cannot be changed.");
    }

    private void ValidatePublishSchedule(DateTimeOffset now)
    {
        if (StartsAt <= now || EndsAt <= StartsAt || SalesStartAt >= SalesEndAt || SalesEndAt > StartsAt || Seats.Count == 0)
            throw new InvalidOperationException("Event schedule or seats are invalid for publishing.");
    }
}
