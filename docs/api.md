# API quick reference

Base URL: `http://localhost:5000`. Swagger is exposed per service in Development.

| Area | Routes |
|---|---|
| Auth | `POST /api/auth/register`, `login`, `refresh`, `revoke`; `GET /api/auth/me` |
| Events | `GET /api/events`, `/api/events/{id}`, `/api/events/{id}/seats` |
| Admin | `POST/PUT /api/admin/events`; `POST /publish`, `/cancel` |
| Booking | availability, seat holds, bookings, booking history |
| Payment | `POST /api/payments` with `Idempotency-Key`; `GET /api/payments/{id}` |
| Realtime | `/hubs/seat-availability` |

Errors use `application/problem+json` and include `traceId`. IDs are UUIDs. Times are UTC ISO 8601. Money combines decimal amount with ISO 4217 currency.
