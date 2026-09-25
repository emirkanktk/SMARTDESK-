# Architecture Decision Records

Short-form ADRs. New entries go at the bottom, numbered sequentially. Don't edit a merged
ADR's decision after the fact - if it changes, write a new ADR that supersedes it and say
so in both.

## ADR-0001: Onion Architecture with CQRS + MediatR

**Decision:** Backend is split into Domain / Application / Infrastructure / API projects,
dependencies pointing inward only; every write/read goes through a MediatR
Command/Query handler rather than a traditional service-layer class.

**Why:** the project brief calls for it explicitly, and it keeps business rules (the
reservation overlap check especially) out of controllers and out of the database access
code, in one place that's easy to unit test without spinning up ASP.NET Core or a real
database.

## ADR-0002: DTOs at the API boundary, never EF entities

**Decision:** Controllers and MediatR handlers never return `SmartDesk.Domain` entities or
EF Core-tracked types directly over HTTP. Each endpoint has its own response DTO.

**Why:** keeps the wire contract (`docs/API_CONTRACTS.md`) stable independent of internal
schema changes, and avoids accidentally serializing EF navigation properties into circular
JSON or leaking fields (e.g. Identity internals) that shouldn't cross the API boundary.

## ADR-0003: ASP.NET Core Identity for users and roles, not a hand-rolled table

**Decision:** `ApplicationUser : IdentityUser<Guid>` plus `IdentityRole<Guid>`, using the
framework's own role/claims machinery (`Roles.Admin`, `Roles.Employee`), rather than a
plain `Role` string column on a custom `User` table.

**Why:** password hashing, token providers, and role/claims-based `[Authorize]` come for
free and are well-tested; a hand-rolled equivalent would just be re-implementing a subset
of Identity with more room for mistakes. `ApplicationUser` lives in
`SmartDesk.Infrastructure`, not `Domain` - see `docs/ARCHITECTURE.md` §3.1 for why.

## ADR-0004: Reservation overlap prevention is a backend guarantee, not a frontend check

**Decision:** The frontend may disable/grey out unavailable slots for UX, but the backend
independently re-validates on every `CreateReservationCommand` and is the only thing that
can actually reject a request. See `docs/DATA_MODEL.md` for the exact rule and the still-open
question about how to make it safe under concurrent requests (application-level
check-then-insert vs. a Postgres exclusion constraint) - to be settled before that handler
is implemented.

**Why:** a frontend-only check is a race condition waiting to happen (two people booking
the same room in the same few seconds) and is trivially bypassable by calling the API
directly.

## ADR-0005: Secrets never in source control

**Decision:** The JWT signing key lives in `dotnet user-secrets` for local development
(never in `appsettings*.json`). `appsettings.Development.json` may hold the local Docker
Postgres connection string because it isn't a real secret (fixed dev-only credentials for
a container nobody outside the local machine can reach) - see `.gitignore` and the
README's setup steps.

**Why:** project brief requirement; also just good practice for a repo more than one
person pushes to.

## ADR-0006: CORS origins are configuration, not a hardcoded allow-list

**Decision:** allowed frontend origins live in config (`Cors:AllowedOrigins` in
`appsettings.Development.json`, currently just `http://localhost:4200`, Angular's default
dev port), read into an `AddCors` policy in `Program.cs` rather than hardcoded in code.

**Why:** caught while checking the skeleton actually works end-to-end rather than just
compiling - `ng serve` on `localhost:4200` calling the API on `localhost:7065` is a
cross-origin request by definition, and without a CORS policy the browser blocks every
frontend HTTP call outright. Verified with a real request: `curl -H "Origin:
http://localhost:4200"` gets `Access-Control-Allow-Origin` back, an unlisted origin
doesn't. Config (not code) so each environment can set its own allowed origins without a
rebuild once there's a real deployed frontend URL to add.

## ADR-0007: Demo users are seeded in Development only; roles in every environment

**Decision:** On startup the API seeds the `Admin` and `Employee` roles in every environment
(the app can't work without them). The demo accounts (`admin@smartdesk.local`,
`employee1@smartdesk.local`, `employee2@smartdesk.local`, shared password listed in the
README) are created only when `ASPNETCORE_ENVIRONMENT=Development`. Seeding is idempotent
and skips itself with a warning if migrations haven't been applied yet.

**Why:** the brief asks for seed data and sample login details in the README, so anyone can
clone, run, and log in. A known password is fine for a local database nobody else can
reach, but must never exist in a shared or deployed environment - gating on the
environment, not on a config flag someone could forget to flip, keeps it that way. Two
employees exist so the SignalR scenario (user A books, user B sees it live) can be demoed
with two browser sessions.

## ADR-0008: Authenticated-by-default API, bearer-token-only Identity

**Decision:** A fallback authorization policy requires an authenticated user on every
endpoint that doesn't declare its own `[Authorize]`/`[AllowAnonymous]`; anonymous endpoints
(login, the OpenAPI document) opt out explicitly. Identity is registered with
`AddIdentityCore` + `AddSignInManager` rather than `AddIdentity`.

**Why:** secure by default - a new controller that forgets `[Authorize]` stays closed
instead of silently public. `AddIdentity` also registers cookie authentication, which makes
a forbidden request answer with a 302 redirect to a non-existent login page instead of a
403; this API only uses bearer tokens, so the cookie handlers are left out entirely.
`SignInManager` is still used for password checks because it applies Identity's lockout
(5 failed attempts → 5 minutes), which a bare `UserManager.CheckPasswordAsync` does not.

## ADR-0009: Handlers query through `IApplicationDbContext`; Application references EF Core, not Npgsql

**Decision:** Application defines `IApplicationDbContext` (the four `DbSet`s plus
`SaveChangesAsync`), which `SmartDeskDbContext` implements. Handlers write their LINQ
against it directly. As a result `SmartDesk.Application` references
`Microsoft.EntityFrameworkCore` (the provider-agnostic core). It does not reference
Npgsql; anything PostgreSQL-specific stays in Infrastructure.

**Why:** the alternative, one repository interface per entity implemented in
Infrastructure, would move the queries that *are* the business rules (availability, the
overlap check) out of the handlers and into Infrastructure, which is the opposite of what
ADR-0001 is for. The repositories would also mostly just wrap `DbSet` one method at a
time. With the interface, a handler is still testable in isolation: tests run it against
the real `SmartDeskDbContext` on in-memory SQLite, a real relational provider with no
PostgreSQL needed. The cost is a framework package in Application, accepted knowingly and
limited to EF Core's abstractions.

## ADR-0010: Resource types are migration seed data; demo resources follow ADR-0007

**Decision:** The five resource types are seeded with `HasData` in the model, so they ship
inside a migration with fixed ids and exist identically in every environment. Eight sample
resources are seeded on startup in Development only, including one under maintenance, and
only while the `Resources` table is empty.

**Why:** types are reference data the app can't run without (every resource needs one),
and fixed ids let seeds, tests, and the frontend refer to them reliably. Sample resources are
demo data, so they follow ADR-0007's Development-only rule. The empty-table check means
the seed never re-creates or overwrites resources someone edited by hand.

## ADR-0011: Overlap safety under concurrency - per-resource lock plus exclusion constraint

Settles the open question left in ADR-0004.

**Decision:** Two layers.
1. `CreateReservationCommand` runs in a transaction that first takes a row lock on the
   resource (`SELECT ... FOR UPDATE`, via `IApplicationDbContext.LockResourceForBookingAsync`).
   It then re-reads the resource status, runs the overlap check, inserts, and commits.
   Bookings of the same resource therefore run one at a time, and each sees the previous
   one's committed row. Bookings of different resources don't wait on each other.
2. A PostgreSQL exclusion constraint, `EX_Reservations_NoOverlap`, over
   `("ResourceId" =, tstzrange(start, end, '[)') &&)` on `Confirmed` rows. The database
   itself refuses an overlap, however the row got there: a future code path, a script, or
   manual SQL. If it ever fires through EF, `SmartDeskDbContext` turns the `23P01` error
   into the same `ConflictException` (409) the handler throws.

SignalR events are sent only after commit.

**Why:** a check-then-insert alone has a race window. The constraint alone is correct, but it
behaved badly under load. In a race test, 30 threads were released at the same instant to
book the same slot, repeated over 15 slots. Every slot still ended up with exactly one row,
but in some rounds all 29 losers got **500s**. The waiting inserts had deadlocked on the
constraint (`40P01`).

Retrying on transient errors (`EnableRetryOnFailure`) made that worse. The retried inserts
collided again, deadlocks multiplied, and requests piled up waiting on locks.

With the row lock, the same test (25 slots × 30 simultaneous requests) finished in about
3 s: exactly one 201 and 29 clean 409s per slot, zero deadlocks, zero 500s. The lock also
closes a smaller race: an admin can no longer deactivate a resource between the status
check and the insert.

The constraint is kept even though the lock makes it redundant for requests that go
through the handler. It's what makes rule 5 a property of the data rather than of one code
path. Its translation to 409 was verified against the real database by inserting
overlapping rows directly, bypassing the handler.

SQLite, used for unit tests, has neither the lock nor the constraint. Those tests cover the
handler logic; the concurrency guarantees were verified against PostgreSQL.

## ADR-0012: "Today" is the caller's local day, passed as an IANA time zone

**Decision:** `GET /api/dashboard` takes an optional `timeZone` (IANA id, e.g.
`Europe/Istanbul`, validated against the system's time zone database). The server converts
the local midnights on either side of "now" into UTC and queries that range. It returns the
bounds it used (`todayStart`, `todayEnd`) along with the data. Everything else stays in UTC,
as the conventions in `docs/API_CONTRACTS.md` already require.

**Why:** timestamps are stored and exchanged in UTC, but "today" is a local concept. With a
UTC day, a user in Istanbul (UTC+3) would see yesterday's bookings as "today" until 03:00.
Sending a zone id rather than an offset keeps daylight-saving days right, because a local
day can be 23 or 25 hours long, and the server has the zone rules to handle that. Returning
the bounds means the screen and the server never silently disagree about which day is
shown.

## ADR-0013: Reservation updates - what can change when, and how updates stay race-free

**Decision:** `PUT /api/reservations/{id}` is a full replace of resource, start, end and note.
What may change depends on the reservation's state:
- **Upcoming:** anything can change.
- **In progress:** only the end and the note can change. This covers the common "the meeting
  runs over" case; the start is in the past and the resource is in use.
- **Ended or cancelled:** nothing can change.

A change of resource or time is checked exactly like a new booking. The only difference is
that the reservation's own current slot is excluded from the overlap check.

Concurrency extends ADR-0011:
- Update and cancel both take a row lock on the reservation (`SELECT ... FOR UPDATE`)
  before reading it, so they serialize with each other.
- A change of resource or time additionally takes the target resource's lock, like create
  does.
- A transaction takes at most one reservation lock followed by at most one resource lock,
  always in that order, so no two transactions can wait on each other in a cycle.

**Why:** "güncelleme" is part of the brief's reservation module. Without the reservation lock,
a cancel and an update racing on the same row could both read "Confirmed". The update could
then broadcast `ReservationUpdated` for a reservation that had just been cancelled.

Verified on PostgreSQL with two tests:
- 10 reservations moved into the same free slot at the same instant: exactly one 200 and nine
  409s.
- Cancel and update of one reservation fired together, 10 rounds: every round ended in one
  of the two valid serial orders, with 0 errors and 0 deadlocks.

## ADR-0014: Reservation events go to every connected client

**Decision:** `SignalRReservationNotifier` sends `ReservationCreated`, `ReservationUpdated`
and `ReservationCancelled` to `Clients.All`. There are no hub groups. The payload is the
same `ReservationDto` as the HTTP API, with enum names. If the send throws, the notifier
logs and returns; the reservation request still succeeds.

**Why:** any signed-in screen that shows a shared calendar or the dashboard needs the same
fact, and the office is one company, so a group per resource would only matter once
connection count makes a full broadcast noisy. The hub stays `[Authorize]`, so anonymous
browsers never receive the payload. Swallowing a failed push keeps the write path
independent of who happens to be connected.

## ADR-0015: Resource blocks - time-boxed unavailability that never overrides a booking

**Decision:**
- An Admin can block a resource for one `[start, end)` window with a required reason, stored
  as `ResourceBlock`. This is separate from `ResourceStatus.Maintenance`, which takes a
  resource out indefinitely.
- While the block exists, booking or moving a reservation into the window gets a 409 that
  quotes the reason, and the availability search leaves the resource out.
- A block can't be created over `Confirmed` reservations. The 409 says how many overlap,
  and the admin cancels or moves those first with the existing endpoints. Nor can it
  overlap another block on the same resource.
- Creating a block takes the per-resource lock from ADR-0011, and so does every booking.
  The booking checks (exists, Active, not blocked, no overlap) live in one place,
  `BookingGuard`, which create and update both call.

**Why:**
- The brief lists "Kaynak bakım/bloke zamanları" as a bonus. `Maintenance` status is all or
  nothing: it can't express "unavailable Thursday 10:00-14:00".
- Cancelling people's reservations automatically when a block lands would be a silent side
  effect with real consequences. Refusing, and saying how many are in the way, keeps that
  decision with a person.
- Sharing the booking lock is what makes the two kinds of claim mutually exclusive without
  a cross-table database constraint, which PostgreSQL can't express. Tested with a block and
  a booking for the same slot released at the same instant, over 12 rounds: exactly one
  succeeded each time, with no errors or deadlocks.
- Blocks deliberately have no exclusion constraint of their own. They're Admin-only and
  always written under the lock, so the lock already rules out duplicates. Reservations
  keep theirs because of the brief's explicit concurrency rule.
