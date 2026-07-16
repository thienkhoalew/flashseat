---
name: verify-flashseat
summary: Drive the FlashSeat Docker stack through customer booking flows.
---

# Verify FlashSeat

1. Start with `docker compose -f deploy/docker-compose.yml up -d --build`.
2. Require all HTTP services to report healthy in `docker compose -f deploy/docker-compose.yml ps`.
3. Open `http://localhost:5173`; login with the development customer account.
4. Drive event discovery, seat hold, booking, successful payment, booking history.
5. Probe two customers concurrently holding one seat; expect exactly `201` and `409`, no `500`.
6. Probe failed payment; expect booking `Cancelled` and seat `Available`.
7. Confirm the notification worker logs one simulated confirmation email.
8. Check recent service logs for unhandled exceptions and HTTP 500 responses.
