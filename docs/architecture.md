# Architecture

## Context

FlashSeat is a portfolio system for demonstrating a reliable ticket-booking flow. The browser reaches backend services only through a YARP gateway.

## Service boundaries

| Component | Owns |
|---|---|
| Identity | Users, password hashes, roles, refresh tokens, JWT issuance |
| Events | Event metadata, publishing, static seats and prices |
| Booking | Dynamic seat inventory, holds, bookings, realtime availability |
| Payment | Idempotent simulated payment attempts and results |
| Notification Worker | Bounded asynchronous notification processing |
| Gateway | Routing, correlation, CORS, headers and rate limiting only |

Domain and Application projects never depend on Infrastructure. API projects are composition roots. EF Core is used directly; no generic repository wrapper.

## Data ownership

Local development shares one PostgreSQL server for cost and convenience. Each service uses a distinct database and credentials where practical. Cross-service changes use immutable integration events, never direct database queries.

## Consistency

- Seat acquisition: short Redis locks plus an atomic PostgreSQL conditional update.
- Business event publishing: transactional outbox.
- Message delivery: at-least-once.
- Message consumption: inbox or unique constraints.
- Browser updates: SignalR hints plus HTTP refetch as recovery.

Detailed race handling belongs in `docs/concurrency.md` during Booking Phase 3.

## Dependency direction

```mermaid
flowchart LR
  Api[API composition root] --> Application
  Api --> Infrastructure
  Infrastructure --> Application
  Infrastructure --> Domain
  Application --> Domain
  Application --> Contracts
```

## Runtime sequence

```mermaid
sequenceDiagram
  actor User
  participant Gateway
  participant Booking
  participant PostgreSQL
  participant Payment
  participant RabbitMQ

  User->>Gateway: Hold selected seats
  Gateway->>Booking: POST /seat-holds
  Booking->>PostgreSQL: Atomic conditional update + hold
  PostgreSQL-->>Booking: Commit
  Booking-->>User: Hold and expiry
  User->>Booking: Create pending booking
  Booking->>PostgreSQL: Booking + outbox
  User->>Payment: Simulate payment
  Payment->>RabbitMQ: Payment result
  RabbitMQ->>Booking: PaymentSucceededV1
  Booking->>PostgreSQL: Confirm booking + mark seats booked
```
