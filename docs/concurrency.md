# Seat hold concurrency

## Guarantee

PostgreSQL is authoritative. Redis locks reduce contention; they are not the correctness boundary.

## Hold algorithm

1. Validate 1–6 unique seat IDs.
2. Sort IDs to give every request the same lock order.
3. Acquire one Redis lock per seat with a unique owner token and 10-second TTL.
4. Start a short PostgreSQL transaction.
5. Run one conditional update over every requested row. Eligible rows are `Available`, or expired `Held` rows.
6. Compare affected rows with requested seats. Any mismatch rolls back the entire transaction and returns HTTP 409.
7. Insert hold and immutable price snapshots; commit.
8. Release only locks still owned by this request using a Lua compare-and-delete script.

The transaction never waits for external HTTP or broker calls.

## Why double booking fails

Concurrent requests may both reach the service. The Redis layer normally serializes them. If Redis expires or fails to serialize, PostgreSQL row conditions permit only the first update from `Available` to `Held`; the second update affects zero rows and rolls back. Unique inventory index `(EventId, SeatId)` prevents duplicate inventory rows.

## Expiration

A `BackgroundService` scans bounded batches every 15 seconds. It only releases inventory still marked `Held`, never `Booked`. Hold reads also treat expired holds as available, avoiding stale UX between scans.

## Production upgrade ceiling

The MVP worker assumes one effective database writer per expired hold. Multiple replicas should claim batches with PostgreSQL `FOR UPDATE SKIP LOCKED`. Add this before horizontally scaling Booking replicas.
