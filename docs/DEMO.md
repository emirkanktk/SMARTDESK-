# SmartDesk Demo Runbook

A 15-minute walkthrough of the backend through Swagger UI, in the order the brief's scenarios
build on each other. Each step says what to do, what should happen, and where the behaviour
lives, so whoever presents can also answer "how does that work?".

## Before the demo

1. Start PostgreSQL and the API (full setup in the README):
   ```bash
   docker compose up -d
   dotnet ef database update --project src/SmartDesk.Infrastructure --startup-project src/SmartDesk.API
   dotnet run --project src/SmartDesk.API --launch-profile http
   ```
2. Open `http://localhost:5291/health` - it must say `Healthy`. Then open
   `http://localhost:5291/swagger`.
3. Start from a clean slate. Demo users and resources are seeded automatically; leftover
   reservations from rehearsals can be removed (local database only):
   ```bash
   docker exec smartdesk-postgres psql -U smartdesk -d smartdesk -c 'DELETE FROM "Reservations";'
   ```
4. Pick a demo day in the future and use it wherever the steps say `DAY` (e.g. `2026-10-05`).
   Times below are UTC (`Z`); 10:00Z is 13:00 in Istanbul.

Demo accounts (all `SmartDesk123!`): `employee1@smartdesk.local` (Ayşe Yılmaz),
`employee2@smartdesk.local` (Mehmet Demir), `admin@smartdesk.local`.

## Walkthrough

### 1. Sign in - `AuthController`, `LoginCommandHandler`, ADR-0008

- `POST /api/auth/login` as employee1 → `accessToken`. Click **Authorize** and paste it.
- `GET /api/auth/me` → Ayşe, role `Employee`.
- Try a wrong password → `401 "Invalid email or password."`: the same answer for an unknown
  email or a locked account, so nobody can probe which emails exist.
- Point out: every endpoint needs a token unless marked anonymous (fallback policy).

### 2. Resources and availability - `GetResourcesQuery`, `GetAvailableResourcesQuery`

- `GET /api/resources` → 8 seeded resources. `?status=Maintenance` → only Camera C-1.
- `GET /api/resources/available?startDateTime=DAYT11:00:00Z&endDateTime=DAYT12:00:00Z`
  → all Active resources; Camera C-1 is never listed.

### 3. Book, and the brief's conflict example - `CreateReservationCommandHandler`, `ReservationOverlap`

- `POST /api/reservations` as employee1:
  `{ "resourceId": "<Meeting Room A>", "startDateTime": "DAYT10:00:00Z", "endDateTime": "DAYT11:30:00Z", "note": "Sprint planning" }`
  → **201**.
- Sign in as employee2 and book Meeting Room A `11:00-12:00` → **409**
  `"Meeting Room A is already reserved for part of that time."` - exactly the brief's example.
- Book `11:30-12:30` instead → **201**. Ranges are half-open, so back-to-back is fine.
- Repeat the availability search for `11:00-12:00` → Meeting Room A is gone from the list.
- Point out: one overlap rule (`Domain/Rules/ReservationOverlap.cs`) shared by search, booking,
  updates and the calendar.

### 4. The other booking rules - validators and `ConflictException`

- Book Camera C-1 → **409** `"... can't be reserved right now (status: Maintenance)."`
- Start yesterday → **400** `"A reservation can't start in the past."`
- End before start → **400** with the field error.
- Point out: 400 = the request itself is wrong (FluentValidation, before the handler);
  409 = the request is fine but clashes with the current data. All errors are ProblemDetails.

### 5. Change and cancel - `UpdateReservationCommandHandler`, `CancelReservationCommandHandler`, ADR-0013

- As employee1, `PUT /api/reservations/{id}` shifting the 10:00-11:30 booking to 10:15-11:30 →
  **200** (it only overlaps its own old slot).
- Move it onto Mehmet's 11:30 slot → **409**.
- As employee2, try to edit employee1's reservation → **403**.
- `POST /api/reservations/{id}/cancel` as the owner → **200** `Cancelled`; call it again →
  **200**, unchanged (idempotent, no second event).
- The freed slot can be booked again.

### 6. Calendar and dashboard - `GetReservationsQuery`, `GetDashboardQuery`, ADR-0012

- `GET /api/reservations?from=DAYT00:00:00Z&to=DAYT23:59:00Z` → the day's bookings with
  `userDisplayName`.
- `GET /api/dashboard?timeZone=Europe/Istanbul` → `todayStart`/`todayEnd` are Istanbul's
  midnights in UTC; `inUseNow`, `myUpcoming` and `stats`.
- Point out: "today" is the user's day, not the server's - at 01:30 in Istanbul a UTC server
  would still be on yesterday.

### 7. Admin - `ResourcesController`, `[Authorize(Roles = Admin)]`

- As employee1, `POST /api/resources` → **403** (not a redirect).
- Sign in as admin: create a resource → **201** with a `Location` header; `PUT` it to
  `"status": "Maintenance"` → booking it now gives **409**.

### 8. Concurrency - the part worth slowing down for (ADR-0011)

Two users booking the same room in the same second is the brief's hardest rule. Show it:

```bash
dotnet run --project tools/SmartDesk.BookingRace -- --parallel 30 --rounds 10
```

Each round fires 30 bookings for one slot at the same instant. Expected: every round prints
`201x1  409x29`, then `PASS`. The tool cancels the winning reservations afterwards (`--keep`
leaves them).

How it holds:
1. `CreateReservationCommandHandler` locks the resource row (`SELECT ... FOR UPDATE`) inside
   its transaction, so bookings of one resource run one at a time and each sees the last
   one's committed row.
2. The `EX_Reservations_NoOverlap` exclusion constraint makes PostgreSQL itself reject an
   overlapping Confirmed row; its error becomes the same 409. Show it with
   `docker exec smartdesk-postgres psql -U smartdesk -d smartdesk -c '\d "Reservations"'`.

The ADR also records what didn't work: the constraint alone deadlocked under this load (500s),
and retrying made it worse.

## Talking points

- **Architecture:** Domain → Application → Infrastructure → API, dependencies pointing inward;
  every request is a MediatR command/query; validation runs in a pipeline behaviour before any
  handler (`docs/ARCHITECTURE.md`).
- **Contract first:** `docs/API_CONTRACTS.md` was written before the endpoints so frontend work
  could start in parallel; entities never leave the API, only DTOs.
- **Tests:** 100+ unit tests; handlers run against the real DbContext on in-memory SQLite. CI
  builds and tests every pull request and checks that model changes come with a migration.
- **Decisions are written down:** `docs/DECISIONS.md`, one ADR per non-obvious choice, with the
  reason and what was tried.

## If something goes wrong

| Symptom | Cause / fix |
|---|---|
| Every call returns 401 | The token expired (60 minutes) - log in again and re-Authorize. |
| Login returns 429 | Login rate limit (60/minute in Development) - wait a minute. |
| Login returns 401 with the right password | Five wrong passwords lock the account for 5 minutes - wait, or use another demo user. |
| Log says "Skipping database seed: pending migrations" | Run `dotnet ef database update` and restart the API. |
| `/health` says `Unhealthy` | PostgreSQL isn't reachable - `docker compose up -d`. |
