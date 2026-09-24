# SmartDesk API Contracts

Planned HTTP surface, written ahead of implementation so frontend work can start against a
stable contract. Update this file in the same PR as any endpoint you add, rename, or
change the shape of - it is the source of truth for what the API looks like, not the
controller code.

All endpoints are under `/api`. Authenticated endpoints expect `Authorization: Bearer
<jwt>`. Error responses use `application/problem+json` (RFC 7807 `ProblemDetails`).

## Auth

| Method | Path | Auth | Command/Query | Notes |
|---|---|---|---|---|
| POST | `/api/auth/login` | none | `LoginCommand` | body: `{ email, password }` → `{ accessToken, expiresAt }`. 400 if a field is missing/malformed, 401 on any other failure. |
| GET | `/api/auth/me` | any authenticated user | `GetCurrentUserQuery` | → `{ id, firstName, lastName, email, roles: string[] }` |

- Login failures all return the same 401 `"Invalid email or password."` - unknown email,
  wrong password and locked-out account are deliberately indistinguishable. Five failed
  attempts lock the account for five minutes (ASP.NET Core Identity defaults).
- On top of that, one client (IP) can make at most `RateLimiting:LoginPermitsPerMinute`
  login attempts per minute - 10 by default, 60 in Development. Beyond that the API answers
  **429** with a `Retry-After` header (seconds) and a ProblemDetails body: `"Too many
  sign-in attempts. Please wait a minute and try again."`. The lockout protects one
  account; this limit protects against one client trying many accounts.
- The access token is valid for `Jwt:AccessTokenLifetimeMinutes` (60 by default); there is no
  refresh token yet - on a 401 the frontend sends the user back to login.
- Token claims: `sub` (user id), `email`, `given_name`, `family_name`, one `role` claim per
  role (`Admin` / `Employee`).

## Resource types

| Method | Path | Auth | Command/Query | Notes |
|---|---|---|---|---|
| GET | `/api/resource-types` | any authenticated user | `GetResourceTypesQuery` | → `[{ id, name }]`, sorted by name, for populating forms/filters |

Types are reference data seeded by migration: Meeting Room, Desk, Company Car, Projector,
Camera (fixed ids, same in every environment). There's no endpoint to add types yet.

## Resources

| Method | Path | Auth | Command/Query | Notes |
|---|---|---|---|---|
| GET | `/api/resources` | any authenticated user | `GetResourcesQuery` | optional query params `resourceTypeId`, `status`. Returns every status unless filtered (the Resources screen shows inactive ones greyed out). |
| GET | `/api/resources/{id}` | any authenticated user | `GetResourceByIdQuery` | 404 if it doesn't exist. For the admin edit form. |
| GET | `/api/resources/available` | any authenticated user | `GetAvailableResourcesQuery` | query params: `startDateTime`, `endDateTime`, optional `resourceTypeId`, `minCapacity`. Rules below. |
| POST | `/api/resources` | Admin | `CreateResourceCommand` | body: `{ name, resourceTypeId, capacity, location, description?, features?: string[] }` → 201 with `Location` header. Always created `Active`. |
| PUT | `/api/resources/{id}` | Admin | `UpdateResourceCommand` | body: the POST fields plus `status`. Full replace: omitting `features` removes them all. |

Every resource endpoint returns the same shape:

```json
{
  "id": "…", "name": "Meeting Room A",
  "resourceTypeId": "…", "resourceTypeName": "Meeting Room",
  "capacity": 8, "location": "2nd Floor",
  "status": "Active",            // "Active" | "Inactive" | "Maintenance"
  "description": null,
  "features": ["TV", "Video Conference"]
}
```

**Validation (400):** `name` and `location` required, max 200 chars; `description` max 1000;
`capacity` 1-1000; `resourceTypeId` must exist; at most 20 `features`, each 1-100 chars, no
duplicates ignoring case and surrounding spaces (`"TV"` and `" tv"` clash).

**Availability rules** (`/api/resources/available`):
- Only `Active` resources are returned; `Inactive` / `Maintenance` never are.
- A resource with a block (see Resource blocks) overlapping the window isn't returned either.
- A resource is excluded if it has a `Confirmed` reservation overlapping
  `[startDateTime, endDateTime)`. Ranges are half-open, so back-to-back bookings are fine:
  a reservation ending at 11:00 does not block a search starting at 11:00. Cancelled
  reservations never block.
- `endDateTime` must be after `startDateTime`; the window can be at most 31 days.
  `minCapacity`, if given, must be positive. Past windows aren't rejected here (it's a
  read-only search); creating a reservation in the past is rejected by
  `CreateReservationCommand`.
- The result is a snapshot. A slot shown as free can be taken a second later, so
  `POST /api/reservations` re-checks and can still answer 409.
- Send times as UTC (`2026-10-05T11:00:00Z`). An explicit offset (`+03:00`) is converted
  correctly, and a time without an offset is treated as UTC.

## Reservations

| Method | Path | Auth | Command/Query | Notes |
|---|---|---|---|---|
| POST | `/api/reservations` | any authenticated user | `CreateReservationCommand` | body: `{ resourceId, startDateTime, endDateTime, note? }` → 201. The owner is always the caller. |
| GET | `/api/reservations/{id}` | any authenticated user | `GetReservationByIdQuery` | one reservation, any status; 404 if it doesn't exist. For the edit form and for opening a calendar entry. |
| PUT | `/api/reservations/{id}` | owner or Admin | `UpdateReservationCommand` | body: `{ resourceId, startDateTime, endDateTime, note? }` - full replace → 200. Rules below. |
| POST | `/api/reservations/{id}/cancel` | owner or Admin | `CancelReservationCommand` | → 200 with the cancelled reservation. Idempotent: cancelling an already-cancelled reservation is a no-op, not an error, and sends no second event. |
| GET | `/api/reservations/mine` | any authenticated user | `GetMyReservationsQuery` | the caller's reservations - upcoming, past and cancelled - oldest start first |
| GET | `/api/reservations?from=&to=&resourceId=&resourceTypeId=` | any authenticated user | `GetReservationsQuery` | the calendar: `Confirmed` reservations overlapping `[from, to)`, optionally for one resource or type, earliest first. `to` must be after `from`, at most 42 days apart - a six-week month grid (400 otherwise). Everyone sees who booked what - it's a shared office calendar. |

Every reservation endpoint returns this shape; it is also the payload of the SignalR events:

```json
{
  "id": "…", "resourceId": "…",
  "resourceName": "Meeting Room A", "resourceTypeName": "Meeting Room", "resourceLocation": "2nd Floor",
  "userId": "…",
  "startDateTime": "2026-10-05T10:00:00Z", "endDateTime": "2026-10-05T11:30:00Z",
  "status": "Confirmed",          // "Confirmed" | "Cancelled"
  "note": "Sprint planning",
  "userDisplayName": "Ayşe Yılmaz"  // who booked it: "First Last", or the email if both are blank
}
```

A reservation appears in a calendar range exactly when it would block a booking of that range
(same half-open overlap rule as availability and create).

**Create - errors**

| Status | When |
|---|---|
| 400 | `endDateTime` not after `startDateTime`; `startDateTime` more than 5 minutes in the past; longer than 31 days; `note` over 500 chars |
| 404 | the resource doesn't exist |
| 409 | the resource isn't `Active` (`"Camera C-1 can't be reserved right now (status: Maintenance)."`), it has a block overlapping the range (`"Meeting Room A is blocked for part of that time: Projector replacement"`), or it already has a `Confirmed` reservation overlapping `[startDateTime, endDateTime)` (`"Meeting Room A is already reserved for part of that time."`) |

The 409 detail is written to be shown to the user as-is. Overlap uses the same half-open rule as
the availability search, so back-to-back bookings are allowed. It holds under concurrent
requests: of several simultaneous bookings for one slot, exactly one gets 201 and the rest 409
(ADR-0011).

**Cancel - errors:** 403 if the caller is neither the owner nor an Admin; 404 if the
reservation doesn't exist; 409 if it has already ended.

**Update - rules and errors.** The owner never changes, even when an Admin edits.

| State of the reservation | What can change |
|---|---|
| upcoming (not started) | anything - resource, start, end, note. The new start follows the create rule (not in the past, 5-minute grace → 400 otherwise) |
| in progress (started, not ended) | only `endDateTime` (must stay in the future) and `note`; changing the start or the resource → 409 |
| ended, or cancelled | nothing → 409 |

When the resource or the times change, the new slot goes through the same checks as a new
booking: the resource must exist (404) and be `Active` (409), and it must not overlap another
`Confirmed` reservation (409). The reservation's own current slot doesn't count, so shifting
10:00-11:00 to 10:30-11:30 works. A change to the note alone skips those checks, so a note
can still be edited after the resource went into maintenance. Other errors: 400 for the
same shape rules as create, 403 for someone who is neither the owner nor an Admin, 404 for an
unknown reservation. A PUT that changes nothing returns 200 and sends no event.

## Resource blocks

A block takes a resource out of booking for one window - planned maintenance, an event, a
car at the service - without changing its status. Blocks use the same half-open
`[start, end)` rule as reservations.

| Method | Path | Auth | Command/Query | Notes |
|---|---|---|---|---|
| GET | `/api/resource-blocks?from=&to=&resourceId=` | any authenticated user | `GetResourceBlocksQuery` | blocks overlapping `[from, to)`, earliest first; same 42-day limit as the calendar. Drawn next to the calendar's reservations. |
| POST | `/api/resource-blocks` | Admin | `CreateResourceBlockCommand` | body: `{ resourceId, startDateTime, endDateTime, reason }` → 201 |
| DELETE | `/api/resource-blocks/{id}` | Admin | `DeleteResourceBlockCommand` | → 204; 404 if it doesn't exist |

```json
{
  "id": "…", "resourceId": "…", "resourceName": "Meeting Room A",
  "startDateTime": "2026-10-05T10:00:00Z", "endDateTime": "2026-10-05T14:00:00Z",
  "reason": "Projector replacement",
  "createdByUserId": "…"
}
```

**Create - errors**

| Status | When |
|---|---|
| 400 | start more than 5 minutes in the past; end not after start; longer than 366 days; `reason` empty or over 200 chars |
| 404 | the resource doesn't exist |
| 409 | another block on the resource overlaps (`"Meeting Room A is already blocked for part of that time."`), or `Confirmed` reservations overlap (`"2 confirmed reservation(s) on Meeting Room A overlap this window. Cancel or move them first."`) |

Existing reservations are never cancelled by a block. The admin cancels or moves them first,
with the reservation endpoints, and then blocks. While a block exists, booking or moving a
reservation into its window returns 409 with the block's `reason`, and availability search
leaves the resource out.

## Dashboard

| Method | Path | Auth | Command/Query | Notes |
|---|---|---|---|---|
| GET | `/api/dashboard?timeZone=Europe/Istanbul` | any authenticated user | `GetDashboardQuery` | today's reservations, resources currently in use, the caller's upcoming reservations |

`timeZone` is an IANA id - in the browser, `Intl.DateTimeFormat().resolvedOptions().timeZone`.
It decides what "today" means: the server runs on UTC, so without it 01:30 in Istanbul would
still count as yesterday. Omitted → UTC. Unknown id → 400.

```json
{
  "generatedAt": "2026-09-23T15:13:00Z",
  "timeZone": "Europe/Istanbul",
  "todayStart": "2026-09-22T21:00:00Z",   // local midnight, as UTC
  "todayEnd": "2026-09-23T21:00:00Z",     // next local midnight (23 or 25 h later on DST days)
  "stats": { "activeResources": 7, "resourcesInUseNow": 1, "reservationsToday": 2, "myUpcomingReservations": 1 },
  "today": [ /* every Confirmed reservation overlapping today, all resources, earliest first */ ],
  "inUseNow": [ /* Confirmed reservations running right now, ending soonest first */ ],
  "myUpcoming": [ /* the caller's next 5 Confirmed reservations that haven't started */ ]
}
```

Every list holds the reservation shape shown under Reservations. `stats.myUpcomingReservations`
is the full count, while `myUpcoming` is capped at 5.

## Real-time (SignalR)

| Hub | Path | Events |
|---|---|---|
| `ReservationHub` | `/hubs/reservations` | `ReservationCreated`, `ReservationUpdated`, `ReservationCancelled` - each carries enough of the reservation's shape for an open calendar/dashboard to patch its view without a refetch |

All three events carry the reservation object shown under Reservations above, as it is after
the change. For `ReservationUpdated` the resource or the times may be different from
before, so screens should replace the entry by `id` rather than patch it in place. Every
event includes
`userDisplayName`, so a screen can say who just booked or cancelled. Handlers raise them
through `IReservationNotifier` only after the change is committed. Until the SignalR
implementation of that interface is registered, a no-op one is in place: every endpoint
works, but open screens don't update live yet.

## Conventions

- Timestamps are UTC ISO-8601 over the wire; the frontend converts to local time for
  display. A timestamp sent without an offset is treated as UTC.
- Enums are serialized as their names (`"Active"`, `"Maintenance"`), not numbers, both in
  responses and in request bodies/query strings.
- List endpoints that can grow (reservations, resources) should be paginated once real
  data volume shows up - not needed for the MVP's expected data size.
- DTOs are never the EF entities directly (see `docs/DECISIONS.md`, ADR-0002).
- Every endpoint requires an authenticated user unless it's explicitly marked anonymous
  (ADR-0008) - "none" in an Auth column above means `[AllowAnonymous]`.
- Error status codes: 400 validation (`errors` holds a field → messages map), 401 missing/
  invalid token or failed login, 403 authenticated but not allowed (e.g. non-Admin), 409
  business-rule conflict (e.g. overlapping reservation), 429 too many login attempts, 500
  unexpected. Validation messages are always English, whatever the server's locale.

## Health

| Method | Path | Auth | Notes |
|---|---|---|---|
| GET | `/health` | none | `200 Healthy` when the API can reach PostgreSQL, `503 Unhealthy` otherwise. Plain text, for container health checks and uptime monitors. |
