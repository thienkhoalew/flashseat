namespace FlashSeat.Events.Domain;

public sealed class Seat
{
    private Seat() { }

    public Seat(Guid id, Guid eventId, string section, string row, int number, decimal price, string currency)
    {
        Id = id;
        EventId = eventId;
        Section = section;
        Row = row;
        Number = number;
        Price = price;
        Currency = currency;
    }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public string Section { get; private set; } = string.Empty;
    public string Row { get; private set; } = string.Empty;
    public int Number { get; private set; }
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = "VND";
    public EventEntity Event { get; private set; } = null!;
}
