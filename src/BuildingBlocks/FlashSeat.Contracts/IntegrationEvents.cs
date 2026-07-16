namespace FlashSeat.Contracts;

public interface IIntegrationEvent
{
    Guid MessageId { get; }
    Guid CorrelationId { get; }
    DateTimeOffset OccurredAt { get; }
    int Version { get; }
}

public sealed record EventSeatV1(
    Guid SeatId,
    string Section,
    string Row,
    int Number,
    decimal Price,
    string Currency);

public sealed record EventPublishedV1(
    Guid MessageId,
    Guid CorrelationId,
    DateTimeOffset OccurredAt,
    int Version,
    Guid EventId,
    string Name,
    IReadOnlyCollection<EventSeatV1> Seats) : IIntegrationEvent;

public sealed record BookingPendingV1(
    Guid MessageId,
    Guid CorrelationId,
    DateTimeOffset OccurredAt,
    int Version,
    Guid BookingId,
    Guid UserId,
    decimal Amount,
    string Currency) : IIntegrationEvent;

public sealed record PaymentSucceededV1(
    Guid MessageId,
    Guid CorrelationId,
    DateTimeOffset OccurredAt,
    int Version,
    Guid BookingId,
    Guid PaymentId,
    Guid UserId,
    decimal Amount,
    string Currency) : IIntegrationEvent;

public sealed record PaymentFailedV1(
    Guid MessageId,
    Guid CorrelationId,
    DateTimeOffset OccurredAt,
    int Version,
    Guid BookingId,
    Guid PaymentId,
    Guid UserId,
    string Reason) : IIntegrationEvent;

public sealed record BookingConfirmedV1(
    Guid MessageId,
    Guid CorrelationId,
    DateTimeOffset OccurredAt,
    int Version,
    Guid BookingId,
    Guid UserId,
    string BookingNumber) : IIntegrationEvent;

public sealed record BookingCancelledV1(
    Guid MessageId,
    Guid CorrelationId,
    DateTimeOffset OccurredAt,
    int Version,
    Guid BookingId,
    Guid UserId,
    string Reason) : IIntegrationEvent;

public sealed record SeatHoldExpiredV1(
    Guid MessageId,
    Guid CorrelationId,
    DateTimeOffset OccurredAt,
    int Version,
    Guid HoldId,
    Guid EventId,
    IReadOnlyCollection<Guid> SeatIds) : IIntegrationEvent;
