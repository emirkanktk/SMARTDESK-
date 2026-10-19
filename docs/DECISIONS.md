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

## ADR-0016: The first admin of a deployed environment comes from configuration

**Decision:** Outside Development, where ADR-0007 seeds no users, `BootstrapAdmin:Email`
and `BootstrapAdmin:Password` (environment variables or a secret store) name the first admin.
On every startup `BootstrapAdminSeeder` creates that account if it doesn't exist and adds it
to the `Admin` role if it isn't there. It never changes an existing account's password.
Setting only one of the two fails startup (`ValidateOnStart`). With neither set and no admin
in the database, it logs a warning. It runs after the demo users, so it works the same in
Development.

**Why:**
- Without it, a deployed database has roles but no users, and there's no endpoint to create
  one. The only way in would be hand-written SQL against Identity's tables.
- Configuration rather than a sign-up endpoint: nothing about it is reachable over HTTP, and
  the password lives where the other secrets live (ADR-0005).
- Never resetting an existing password means leaving the variables in place is harmless. An
  admin who changed their password isn't overwritten on the next restart, and removing the
  variables later changes nothing.
- An existing account with that email is promoted rather than refused. That's the useful
  reading of "make this person the admin" when they already have an account.

## ADR-0017: The usage report measures the office day, in the admin's time zone

**Decision:** `GET /api/reports/usage` (Admin) reports on whole local days, `from` to `to`
(at most 92), in the caller's IANA time zone, the same way as the dashboard (ADR-0012). For
each resource, and summed per type and in total, it gives two figures:
- `bookedHours`: all the confirmed booked time inside the range
- `utilization`: the share of the office day that was booked, where the office day is Monday
  to Friday, 09:00-18:00 local (`ReportRules`)

It also lists the reservation and cancellation counts and the five people with the most
booked hours. It's computed in memory from the reservations overlapping the range, with no
new tables.

**Why:**
- A meeting room is idle every night and weekend by design. Measured against 24/7, a room
  booked all day every weekday would read 27% and every room would look empty. Against the
  office day, the same room reads 100%, which is how people talk about it. Booked time outside
  those hours (a car over the weekend) still shows in `bookedHours`.
- Local days and local office hours, not UTC: in Istanbul "09:00" is 06:00 UTC, and a day
  isn't always 24 hours. Each day's window is converted separately, as the dashboard does with
  midnights.
- 92 days bounds the work per request (one query, a few thousand rows at most), and a quarter
  covers the reviews an admin runs. Computing in C# keeps the clipping to office hours per day
  readable and unit-testable, which in SQL it wouldn't be.
- Rows are Active resources plus anything with reservations in the range. A retired room still
  shows what it was used for, while resources retired long ago don't pad the totals with zeros.
  Past status changes aren't recorded, so availability counts every listed resource for the
  whole range, and blocked windows aren't subtracted. The numbers are "roughly how full", not
  billing.

## ADR-0018: Reservation reminders go to the owner over SignalR, once, from a background job

**Decision:**
- **Choice:** the owner chooses a reminder per reservation, 5, 15, 30 or 60 minutes before the
  start, or none. It's stored on the reservation as `ReminderMinutes`, with `ReminderSentAt`.
- **Sending:** a hosted service (`ReservationReminderWorker`) runs `SendDueRemindersCommand`
  every 30 seconds. It finds reminders whose moment has passed for Confirmed reservations that
  haven't started, marks each one sent under the reservation's lock (the same lock update and
  cancel take, ADR-0013), and then sends `ReservationStartingSoon` to the owner only, with
  `Clients.User`.
- **Re-arming:** moving the start, or choosing another reminder, clears `ReminderSentAt`.
- **Delivery:** in-app, plus a desktop notification when the browser allows one. No email.

**Why:**
- The brief lists "Rezervasyon başlamadan önce bildirim" as a bonus. The hub and the per-user
  identity SignalR already has, from the token's subject, make "tell this user" one call, with
  no mail server, templates or addresses to manage.
- Per reservation, not a global setting: a 15-minute nudge suits a room down the hall, while a
  company car across town wants an hour.
- Marking before sending, under the lock, means exactly-once is decided in the database. A
  reservation moved or cancelled while its reminder was being prepared is re-read, and skipped
  if it's no longer due. A failed push is then lost rather than repeated every 30 seconds, which
  is the better failure for a nudge.
- "Due" is decided in memory among the reservations starting within the longest option (an
  hour), because "start minus this row's minutes" isn't translated the same way by every
  database provider. A partial index keeps that scan to the rows still waiting.
- Not queued for offline users: a reminder that arrives after the meeting started is noise.
  The dashboard already lists your upcoming reservations for when you come back.

## ADR-0019: Password reset by emailed link; email is queued and sent in the background

**Decision:**
- **Flow:** `POST /api/auth/forgot-password` always answers 202. For an existing account it
  queues an email with a link to the frontend's reset page, carrying Identity's password-reset
  token (base64url). The link lasts 2 hours and is single-use, since a reset changes the
  security stamp. `POST /api/auth/reset-password` checks the token and sets the new password
  under Identity's policy. An unknown email, and a wrong, used or expired token, all get the
  same "invalid or expired link" answer.
- **Sending:** handlers call `IEmailSender.Queue`, which puts the message on a bounded
  in-memory channel. `EmailDispatchWorker` sends it over SMTP (MailKit), retrying twice. With
  no SMTP host configured, it writes the email to the log instead, but only in Development:
  anywhere else `Email:Smtp:Host` is required and the API refuses to start without it.
  Development uses Mailpit from docker-compose. `App:FrontendUrl` is where links point, and
  it's required at startup.

**Why:**
- The brief-level expectation for a real product: users forget passwords, and an admin
  resetting them by hand doesn't scale. Email is the channel that proves the person owns the
  address.
- Not revealing which emails have accounts means more than an identical response body. It
  needs identical timing too: sending inline would make known emails answer slower. Queueing
  makes both paths do the same work before responding (sign-in got the same treatment in #38).
- A mail outage must not break sign-in flows or requests. The queue absorbs it, retries cover
  a blip, and a dropped message is logged rather than failing the request.
- Mailpit lets the whole flow be exercised locally, with the real SMTP code path, without
  sending anything to real inboxes.
- The log fallback writes whole emails, and a reset link in a log is a working key to that
  account for 2 hours - the bootstrap admin's included, since anyone can ask for a link. A
  production started without mail settings would quietly hand every account to whoever reads
  the logs. So, as with a half-configured bootstrap admin (ADR-0016), a missing SMTP host stops
  startup outside Development instead.
- **Trade-offs:** the queue is in memory, so messages still queued are lost if the API stops,
  and the user can simply ask again. Data-protection keys aren't persisted in the Docker stand-in,
  so a restart invalidates outstanding links there.

## ADR-0020: Accounts have a status and a team; Managers sign up and run their own team

**Decision:**
- **Roles:** `Admin`, `Manager`, `Employee`, one per account. Resources and the calendar stay
  shared by everyone; a Manager differs from an Employee only in managing a team.
- **Status:** every account is `Active`, `PendingApproval` or `Disabled`. Only an Active
  account can sign in (`403` with a readable message otherwise, and only after the right
  password) or use a token.
- **Sign-up:** `POST /api/auth/register` creates a Manager in `PendingApproval` and emails the
  applicant and every active Admin. An Admin approves it (Active, applicant emailed) or rejects
  it (deleted, applicant emailed). For an email that already has an account it changes nothing,
  emails the owner, and gives the same 202.
- **Teams:** an Employee may have a `ManagerId`, always an active Manager. A Manager adds team
  members, takes them out of the team, disables or re-enables them and sends them reset links.
  Anyone outside their team answers 404. An Admin does all of that for every account, and also
  changes roles and moves Employees between teams.
- **Accounts made by someone else** are Active with no password. Their owner gets an
  invitation carrying a password-reset link (ADR-0019), so nobody else ever knows the password.
- **Tokens follow the account:** `JwtBearerEvents.OnTokenValidated` asks `IUserAccessChecker`
  whether the token's user is still Active and has exactly the token's roles. If not, the
  request is unauthenticated. The answer is cached in memory for 30 seconds per user, and
  `UserAccounts` drops the entry whenever it changes that user.
- **Rules that keep it manageable:** nobody changes their own role or disables themselves.
  There is always an active Admin. A Manager with team members keeps the role until the team
  is moved. Only an unapproved sign-up is ever deleted. `BootstrapAdmin` (ADR-0016) also makes
  its account Active again, as the way back in.

**Why:**
- The brief's users are an office, not the public: letting anyone create a working account
  would hand out access to company resources. A pending state keeps sign-up self-service
  while an Admin still decides who gets in. Managers adding their own people spreads that
  work without making everyone an Admin.
- Anti-enumeration carries over from sign-in and password reset (#38, ADR-0019). Registration
  checks password rules before looking the email up, and does the same hashing work either
  way, so neither the answer nor its timing shows whether an email is registered. Inside the
  app, a Manager or Admin adding a colleague is told plainly that an email is taken: they are
  signed in, and that answer is what they need.
- A JWT is otherwise good until it expires (60 minutes). Without the check, a disabled user,
  or an Admin demoted to Employee, would keep their old access for up to an hour. A cached
  lookup costs one small query per user per 30 seconds. That is cheaper than short-lived
  tokens plus refresh tokens, which the frontend doesn't have yet.
- The last-Admin rule is counted before the change and again after it. Two Admins demoting
  each other at the same moment would both pass the first count; the second count finds no
  Admin left and undoes that change, so the system can't lock itself out.
- Disabling rather than deleting keeps reservations and reports intact. A person who left
  still shows up in last month's usage.
- **Trade-offs:** the cache is per process. With several API instances, a change made on one
  would reach the others within the 30 seconds rather than at once. Open SignalR connections
  aren't cut when an account is disabled: they keep receiving calendar events until they
  reconnect. A disabled user's future reservations stay booked until an Admin cancels them.
  Invitation links last 2 hours like any reset link, and "Forgot password" issues a new one.

## ADR-0021: Employees join a team through their manager; accounts made by others get temporary passwords

**Decision:**
- **Joining a team:** the public sign-up takes an optional manager's email. With it, the
  applicant becomes a pending Employee. If the address is an active Manager's, the request
  joins that team, and that manager approves or rejects it under My team
  (`/api/team/requests/{id}/approve|reject`). Otherwise the request has no team, and the
  Admins get an email with the address the applicant typed; an Admin can approve it into a
  team (`POST /api/users/{id}/approve` with `managerId`). Without a manager's email, sign-up is
  a Manager waiting for an Admin, as in ADR-0020.
- **Accounts made by an Admin or a Manager** get a temporary password chosen by that person
  and handed over in person. The account is flagged `MustChangePassword`. The owner is emailed
  that the account exists, never the password. The emailed invitation link from ADR-0020 is
  gone. An Admin (anyone) or a Manager (their team) can set a new temporary password later
  (`/api/users/{id}/password`, `/api/team/members/{id}/password`). That flags the account
  again, ends its sessions, lifts a lockout, and tells the owner by email.
- **Forced change on the server:** while the flag is set, every endpoint except
  `GET /api/auth/me` and `POST /api/auth/change-password` answers 403 with
  `code: password-change-required` (`PasswordChangeGate`, after authorization). Changing the
  password, or a reset link (ADR-0019), clears the flag.
- **Sessions follow the password:** tokens carry `sst`, a digest of the security stamp they
  were issued under, and `OnTokenValidated` compares it with the account's current stamp.
  Identity moves the stamp on every password change, so each change ends all earlier sessions.
  `change-password` returns a fresh token for the session that made it.
- **Team and flags come from the server:** `OnTokenValidated` adds the user's current team and
  the "must change password" state to the request, from the same cached access check as the
  status and roles. It strips any such claims a token might carry.
- **Real-time connections end with the access:** `HubConnectionRegistry` keeps each user's hub
  connections. Every account change aborts them, so the client reconnects through the token
  checks again. This closes the gap noted in the #53 review.

**Why:**
- An employee knows who their manager is. Asking for that address routes the request to the
  one person who can vouch for them. Sending unknown addresses to the Admins, with an identical
  applicant email, means the form can't be used to find out who the managers are.
- A temporary password handed over in person is how offices usually onboard, and it doesn't
  depend on the new person's mailbox working on day one. Forcing the change on the server
  makes sure a password someone else knew is never the one in use. Hiding screens in the
  frontend would leave the API itself open.
- Tying tokens to the security stamp is the standard way to make "change your password" also
  mean "sign out everywhere". Identity already maintains the stamp, and the check rides on the
  lookup ADR-0020 does anyway.
- **Trade-offs:** tokens issued before this change have no `sst`, so everyone signs in once
  more after the upgrade. The 30-second cache and the connection registry are per process
  (ADR-0020's note on several instances applies).

## ADR-0022: Resources belong to teams, or are shared by everyone

**Decision:**
- **Ownership:** `ResourceTeam (ResourceId, TeamId)`, many-to-many. A team is identified by its
  Manager's account id, as in ADR-0020, and there's no foreign key across the Identity
  boundary. A resource with no teams is shared by everyone. One with teams may be used by
  those teams' members and managers only. One with several teams is theirs to share, first come
  first served, and the overlap rule (business rule 1) keeps the availability honest.
- **Visibility:** `ResourceAccess` holds the rule once, as query filters every handler applies
  for a non-Admin:
  - resources: list, by id, availability
  - the calendar and a reservation by id (the owner always sees their own)
  - the dashboard's office-wide lists and counts
  - blocks
  Another team's resource answers 404, like one that doesn't exist, so its name and schedule
  don't leak. Booking re-checks it inside the booking lock (`BookingGuard`).
- **Management:** Admins create resources for any teams, or shared, and change a resource's
  teams (`PATCH /api/resources/{id}/teams`). A Manager creates resources for their own team
  (always theirs alone). They edit, switch on and off, and block only resources that belong
  to their team alone. A shared resource, or one other teams use too, stays an Admin's (403).
- **Real-time:** a shared resource's events still go to everyone. A team resource's go only
  to the `team:{id}` groups of its teams, to `admins`, and to the reservation's owner, who
  keeps seeing their own reservation even after leaving the team. Each connection joins these groups
  from the account as it is now. A team change aborts the user's connections (ADR-0021), so
  the reconnect joins the right group.
- **Consistency:** a Manager whose team has resources can't be given another role until the
  resources are shared or given to another team, just as with team members (ADR-0020).
  Changing a resource's teams keeps existing reservations, as a status change does.

**Why:**
- Each manager's people use that manager's things; another team's equipment isn't theirs to
  book. When two teams do need the same thing, assigning it to both is simpler and fairer than
  lending it out case by case. Availability already settles who gets which hour.
- Filtering in the query, rather than checking after loading, keeps lists, counts and searches
  consistent with each other, and one set of expressions means they can't drift apart.
- Letting managers run their own team's resources spreads the work without handing them
  anything shared, which affects everyone.
- **Trade-offs:** someone moved to another team keeps the reservations they already made on
  their old team's resources. They can still see and cancel them, but can't make new ones there.
  Group membership is set when a connection opens, so a change made without SmartDesk (directly
  in the database) reaches open connections only on their next reconnect.

## ADR-0023: The API answers in Turkish or English; emails in the recipient's language

**Decision:**
- **Where the language comes from:** each request's `Accept-Language`, through ASP.NET Core's
  request localization, which runs first in the pipeline so error responses are covered too.
  Turkish or English; no header, or any other language, is English, so tools, tests and
  scripts keep today's messages. The frontend sends its own `LOCALE_ID`, not the browser's
  language.
- **Only the UI culture follows it.** Messages are looked up in the UI culture; the formatting
  culture stays English. A Turkish request can't change how the code parses numbers and dates
  or compares text, which rules out the Turkish dotted and dotless "i" problems.
- **Where the texts live:** `Messages.resx` (English, the neutral culture) and
  `Messages.tr.resx` in the Application project. The build generates the `Messages` class, so
  a handler throws `new ConflictException(string.Format(Messages.Booking_Taken, name))` and
  gets the request's language. The English texts are the ones the API has always sent, word
  for word. Validator messages are lambdas (`WithMessage(_ => …)`), looked up when a rule fails,
  since validators outlive a request.
- **FluentValidation and Identity:** FluentValidation's built-in messages follow the UI
  culture, with field names from `Messages` (`Property_Name`: "Ad"). The English field names
  are what FluentValidation shows anyway. Identity's password rules come through a
  `LocalizedIdentityErrorDescriber` whose English is Identity's own wording.
- **Codes for conflicts a screen acts on:** `ConflictException.Code` becomes the problem's
  `code` (`resource-unavailable`, `resource-blocked`, `reservation-overlaps`,
  `block-overlaps-block`, `block-overlaps-reservations`), so a client never has to match on
  wording.
- **Emails:** written in the recipient's language, not the sender's. `AspNetUsers.Language`
  holds the language the user last signed in with. A new account starts with the language of
  the request that made it: the applicant's own on a sign-up, the Admin's or Manager's when
  they add someone. For an account without one yet, the current request's language is used.

**Why:**
- SmartDesk is used in Turkish and English, Turkish by default (issue #58). The frontend
  translates its screens, but part of what a user reads comes from the API: a 409's detail,
  the validation message next to a field, Identity's password rules, every email. The screen,
  its errors and its emails should be in one language. Taking it from the app, not
  the browser, keeps a user who picked English in a Turkish browser in English.
- A request header needs no endpoint and no stored state per request, and it's what HTTP
  clients already send. English as the fallback keeps every existing client and test working
  unchanged.
- Resource files are the platform's own mechanism: satellite assemblies, culture fallback
  (`tr-TR` → `tr`), and a generated class that makes a missing key a compile error. One test
  checks that every key has a Turkish text with the same placeholders.
- An email arrives later, often from someone else's action, such as an approval, so the
  sender's language is the wrong one. The sign-in language is the best signal of what the
  recipient reads, and it updates by itself when they switch.
- **Trade-offs:** messages from ASP.NET Core itself (an unparseable body's "The input was not
  valid.") stay English; the screens never show them. A user's email language changes only
  when they sign in, not the moment they switch languages on screen. A resource or reason
  typed by a person stays as it was typed.

## ADR-0024: A resource leaves service only when no bookings are waiting; bookings start at most 180 days ahead

**Decision** (issue #41, options A2 and B1):
- **Status:** switching a resource to `Maintenance` or `Inactive`, by `PATCH /api/resources/{id}/status`
  or the full `PUT`, is refused with 409 (`resource-has-reservations`) while `Confirmed`
  reservations on it are still to start. The message counts them, and the admin cancels or moves
  them first, as for a blocked window (ADR-0015). Past and running reservations don't count, and
  going back to `Active` is always allowed. The count and the change run in one transaction that
  holds the resource's booking lock (ADR-0011), so a booking can't land in between.
- **Horizon:** a reservation can start at most 180 days ahead (`ReservationRules.MaxAdvance`).
  The same applies when an upcoming reservation is moved, and to the availability search, since
  finding what can't be booked helps nobody. The forms mirror it.

**Why:**
- Until now the status changed and every future booking stayed, pointing at a room under
  maintenance, and nobody was told. Blocks already refuse in the same situation. Using the same
  rule means one behaviour to learn, and each cancellation reaches its owner live.
- Without a horizon, a booking could start in 2099, and one person could hold a room for years.
  Most teams plan within a quarter or two, so 180 days is generous.
- **Trade-offs:** there's still no cap on how many bookings one person holds; it's easy to add
  if it ever becomes a problem. Taking a resource out of service now takes a step more when it
  has bookings, but that step is where the owners find out.

## ADR-0025: The UI in Turkish and English, from one build, Turkish by default

**Decision** (issue #58):
- **One build, translations loaded at start.** `@angular/localize` with runtime translations.
  The English texts are written in the code: `i18n="@@screen.key"` in templates and
  `$localize` in code, each with a custom id. The Turkish ones are in
  `public/i18n/tr.json`. `main.ts` reads the stored choice (Turkish by default), and for
  Turkish it fetches `tr.json`, calls `loadTranslations()` and registers the `tr` locale data.
  Only then does it import the app and bootstrap it with `LOCALE_ID` `tr` or `en-US`, so
  dates, numbers and percentages follow the language too. It also sets `<html lang>`.
- **The switch stores and reloads.** TR | EN in the top bar, on the front page and on sign-in.
  The choice is kept per browser (`localStorage`).
- **The API follows.** `languageInterceptor` sends `LOCALE_ID` as `Accept-Language`
  (ADR-0023), so the API's messages come back in the language on screen. The values the API
  sends as data (a resource's status, a role) are turned into words in `core/i18n/labels.ts`.
- **Kept in step:** `npm run i18n:extract` writes the English texts to
  `src/locale/messages.json` and lists what `tr.json` still lacks. `npm run i18n:check` and
  a unit spec fail when a text has no Turkish version, or a Turkish version has different
  placeholders.

**Why:**
- Angular's build-time localization makes one build per language. That means a path per
  language (`/tr/`, `/en/`) and server rewrites to serve them. Loading the translations at
  start keeps one build, one address and no server or Docker change. The cost is 69 kB more
  JavaScript (25 kB gzipped), plus the 31 kB `tr.json`, which only a Turkish session fetches.
- Translations apply when code runs: a compiled template, or a `$localize` string when its
  module loads. Switching without a reload would mean making every text reactive, for
  something a user does once. So the app loads the translations first and imports itself
  after, which is also what makes module-level texts such as route titles work.
- Turkish is the office's language and what the product owner asked for. English stays one
  click away, and it's the language the code and the tests are written in.
- Sentences are whole messages with named placeholders, one per case. The English ones used
  to be assembled from pieces ("a"/"an", "Zeynep Kaya's", "reservation(s)"), and Turkish puts
  the words in another order and adds suffixes that depend on the word. So the Turkish texts
  never hang a suffix on a name ("Zeynep Kaya ekibi", "… adlı kişinin …").
- **Trade-offs:** a Turkish session starts with one more small request. Unit tests run in
  English, apart from one spec that loads the Turkish texts. Custom ids keep a translation
  when the English wording is polished, but a text whose meaning changes needs a new id.
  Names and reasons that people type stay as typed.

## ADR-0026: Short access tokens, renewed from a session in an HttpOnly cookie

**Decision:**
- **Two tokens.** Signing in starts a session. The API returns a 15-minute access token in the
  body, and puts the session's refresh token in the `smartdesk_session` cookie: `HttpOnly`,
  `Secure`, `SameSite=Strict`, `Path=/api/auth`.
- **In memory only.** The app keeps the access token in memory, never in `localStorage`. It
  renews it with `POST /api/auth/refresh` at start-up, a minute before it expires, and when
  the API refuses one.
- **Rotation, with theft detection.** Each renewal replaces the refresh token. The replaced one
  still works for 30 seconds, for two tabs renewing at once; used later, it can only be a
  copy, and the whole session ends. Tabs take turns renewing (Web Locks) and tell each other
  about signing in and out (`BroadcastChannel`). The replacement is a conditional update, so
  two renewals with the same token can't both make one: the second gets an access token and
  no new cookie, since the first answer brings that to the same browser.
- **Only the API ends a session.** A renewal that gets no answer - a laptop waking before its
  Wi-Fi, an API restarting - keeps the user signed in: the app tries again after 10, 20 and
  40 seconds, then every minute, and guards let navigation through, where the page's calls say
  the server can't be reached. A 401 from the refresh endpoint ends it. A browser without the
  cookie gets a 204.
- **A sign-out is finished.** The cookie can't be removed by script, so a sign-out that can't
  reach the server is noted in the browser and finished before any session is taken up again.
- **Ending a session.** A session ends after 14 days without a renewal, after 30 days in any
  case, on sign-out, and when the password changes or the account is switched off. Each access
  token names its session (`sess`), and the per-request check (ADR-0020) refuses a token whose
  session has ended, so signing out ends it on the server at once.
- **Storage.** `UserSessions` keeps a SHA-256 hash of the current and the previous refresh
  token, never the tokens themselves.
- **Same origin in development.** `ng serve` proxies `/api` and `/hubs` to the API, as a
  reverse proxy does in production, so the cookie is a same-site cookie everywhere and needs
  no CORS credentials setup.

**Why:**
- Before this, a 60-minute token in `localStorage` meant two problems. An active user was
  signed out every hour, losing whatever they were typing. And any script that got onto the
  page could read the token and use it for up to an hour. Signing out only forgot the token
  in the browser; the token itself kept working.
- An HttpOnly cookie can't be read by script. `SameSite=Strict` and the narrow path keep it
  out of requests other sites start, which rules out cross-site request forgery on renew and
  sign-out. A 15-minute access token caps what a stolen one is worth.
- Rotating the refresh token turns a stolen copy into a detectable event: whichever side uses
  a replaced token ends the session for both.
- A network error says nothing about the session, which is still open on the server. Ending
  it there would throw away a 14-day session over a Wi-Fi blip. On a shared computer, a
  sign-out that never reached the server would leave the next person signed in.
- The per-request session check rides on the per-user lookup that already checks the
  account's status, roles and stamp. That lookup is cached for 30 seconds and gains one small
  indexed query, so most requests cost nothing extra.
- **Trade-offs:**
  - Each tab renews every quarter of an hour: one small request, not rate limited, since
    guessing a 256-bit token isn't an option.
  - The proxy is one more piece of the dev setup. In return, development behaves like
    production.
  - An access token already issued stays usable until its session's end reaches the cache,
    at most 30 seconds on another API instance.

## ADR-0027: Resource photos, kept in the database and fetched with the token

**Decision:**
- **One optional photo per resource**, set by whoever may edit the resource: an Admin, or a
  Manager for a resource that belongs to their team alone (ADR-0022). Without one, the resource
  shows its type's picture, which ships with the app (`frontend/public/images/resource-types`).
- **In the database.** `ResourcePhotos` keeps the image, at most 2 MB, next to the resource:
  nothing else to back up or deploy, and a resource's photo goes with it.
- **Only real images.** The API recognises JPEG, PNG and WebP by their first bytes and stores
  the type it recognised; the name and type the upload claims don't count. No SVG, which can
  carry script. Photos are served with `X-Content-Type-Options: nosniff`. (Since 2026-10-09,
  also at most 4096 pixels on a side, read from the header without decoding it: a 2 MB file
  could otherwise open into a picture too big for a phone to draw.)
- **Smaller before uploading.** The app draws the chosen picture at most 1600 pixels on its
  longest side and writes it as WebP (JPEG where the browser can't), so a phone photo of
  several MB goes up as a few hundred KB, without the location and the other details the
  camera stored in it.
- **The resource's access rule.** `GET /api/resources/{id}/photo` needs a signed-in user and
  answers 404 for another team's resource. The app fetches it with the token, like any other
  call, and shows it through an object URL.
- **Caching.** Resource responses carry `photoUpdatedAt`, not the photo. The app keeps each
  version of a photo for as long as it runs, and the API sends an `ETag` with
  `Cache-Control: private, no-cache`, so after a reload an unchanged photo costs a 304.

**Why:**
- People pick faster when they recognise the room or the car, and the type's picture keeps
  the screens looking finished before anyone uploads a photo.
- A few dozen photos of a few hundred KB are little for PostgreSQL, and keeping all data in one
  place leaves backups and the Docker setup as they are. A file or object store is the step to
  take if photos ever grow large or many.
- Trusting the upload's name or type would let an HTML or SVG file be stored as a "photo" and
  served from the API's origin. Recognising the bytes and refusing everything else closes that.
- A plain `<img>` URL can't carry the bearer token, and a public one would show other teams'
  resources. Fetching with the token keeps a single access rule.
- **Trade-offs:**
  - Photos travel through the API rather than a CDN; fine at this size.
  - Getting a picture ready needs a browser that can read it. A HEIC photo in a browser that
    can't is refused with a message, and has to be converted first.
  - The type pictures are stock photos under the Unsplash License, credited in the README.

## ADR-0028: Behind a reverse proxy, the client's address comes from trusted forwarded headers

**Decision:**
- The API applies `X-Forwarded-For` and `X-Forwarded-Proto` first thing in the pipeline, so
  the sign-in rate limit, HSTS and the HTTPS redirect see the client's address and scheme.
- The headers are believed only from trusted proxies: one on the same machine, or those listed
  in `ForwardedHeaders:KnownProxies` (addresses) and `ForwardedHeaders:KnownNetworks` (CIDR
  ranges). From anyone else they're ignored.

**Why:**
- Behind a proxy, every connection comes from the proxy. The sign-in rate limit (ADR-0008) is
  per client address, so the whole office would share its 10 attempts a minute. A busy morning
  would lock people out, and one attacker would lock out everyone.
- The proxy talks plain HTTP to the API, so without the scheme the API would take every
  request for HTTP: HSTS would never be sent.
- Anyone can send these headers. Trusting them from any peer would let a client pick a fresh
  address for every attempt and walk around the rate limit.
- **Trade-offs:** a proxy on another machine or container has to be listed, or the API keeps
  seeing only the proxy. The README says how.

## ADR-0029: An optional booking assistant on Claude that proposes and never books

**Decision:**
- **Optional, and off without a key.** With `Assistant:ApiKey` set, signed-in users get an
  assistant: a button in the corner of every screen that opens a conversation. Without a key,
  `GET /api/assistant` answers `enabled: false`, the app shows no button, and
  `POST /api/assistant/messages` answers 503. Nothing else changes.
- **Claude, through Anthropic's official SDK** (the `Anthropic` NuGet package):
  `claude-opus-5-5` at effort `low`, both settings. The instructions and the tool definitions
  are the same for every request and are cached. The API's server-side fallback is on, so a
  question one model declines on policy grounds can still be answered by another.
- **Its tools are the screens' queries, run as the user.** Free resources for a time, resources
  by name, place or equipment, the resource types, and the user's own reservations. Each one is
  a MediatR request like a screen's, with the user's identity: team resources (ADR-0022), blocked
  windows, validation and the booking horizon all apply. The model sees only what the user may
  see.
- **It proposes; the user confirms.** `propose_booking` and `propose_cancellation` only put a
  card under the answer. They check first that the resource is free, or that the reservation is
  the user's and still to come. The card's button calls `POST /api/reservations` or
  `POST /api/reservations/{id}/cancel`, the same endpoints, rules and live updates as the
  screens; a cancellation asks first, as it does everywhere. The assistant has no way to write.
- **Local times.** The app sends the browser's time zone with each question. The model reads
  and writes local times (`2026-10-01T14:00`) and the server converts them. It is told the time
  now, the user's name and role, and the language to answer in.
- **The server keeps nothing.** The app sends the whole conversation with each question, at
  most 40 messages of up to 2,000 characters each. When a card is used, the conversation gets
  a line saying so ("Booked: Room A, …"), which the model reads as the user's.
- **Limits.** 20 questions a minute per user (`RateLimiting:AssistantPermitsPerMinute`), at most
  6 rounds of tool calls per answer (`Assistant:MaxToolRounds`), at most 3 cards per answer. So
  that the limit can count per user, the rate limiter now runs after authentication.
- **One time limit per answer.** 90 seconds in all (`Assistant:TimeoutSeconds`), every round,
  retry and tool included. An answer that runs out of time gets the same 503 as a model that
  can't be reached: try again in a moment. A limit per call wouldn't bound the wait: an answer
  can make 7 calls, and the SDK tries each one up to 3 times.

**Why:**
- The brief lists an optional LLM assistant. It was Burak's; the team took it over when he left
  (#83). "A room for 4 tomorrow at 10" is quicker to say than to fill in on two screens.
- Reusing the queries keeps a single access rule. An assistant with its own data access would
  be one more place to get teams, blocks and time zones right.
- A model can misread "next Monday" or pick the wrong room. The card shows exactly what would
  happen, the booking still goes through every rule, and nothing happens unless the user presses
  a button.
- Off without a key, development, CI and the tests never need a key or spend anything. The tests
  use a scripted model, and the Claude adapter is tested against a stand-in for the API.
- A conversation kept in the browser needs no table and nothing to clean up, and it is gone at
  sign-out.
- **Trade-offs:**
  - Each question costs tokens: Opus 5.5 with cached instructions, often two or three rounds.
    The two limits cap it, and the log records the tokens each answer used.
  - The questions and what the tools return go to Anthropic's API: resource names and places,
    and the user's name and bookings. That is fine for this project. An organization would check
    its data policy before setting a key.
  - The model can still be wrong in what it says. Its cards are the only way it acts, and the
    panel says to check them.
  - A request that carries a token is now authenticated before it is counted. Sign-in requests
    carry none, so the sign-in limit costs what it did.

## ADR-0030: Checking in at the door with a QR code, and releasing bookings nobody used

**Decision:**
- **Per resource, off by default.** An Admin, or a Manager for a resource of their team alone,
  can switch on "bookings must be checked in" (`Resource.CheckInRequired`). Resources without
  it work exactly as before.
- **A QR code at the door.** Every resource has a random 128-bit secret (`CheckInCode`). Its QR
  code opens the app's check-in page with the secret in the address, and the signed-in owner
  checks in there. Only those who may edit the resource see the code: its own endpoint, never
  in resource responses. Replacing it retires the printed one.
- **The window.** From 10 minutes before the start until 15 minutes after, and never once the
  booking is over. Only the owner checks in, only a `Confirmed` booking. (Anyone invited to it
  may too, since ADR-0032. Since ADR-0040 the 15 minutes are a setting, `CheckIn:GraceMinutes`.
  Since ADR-0050, whoever is at the door without an account checks in the booking waiting.)
- **Release.** A booking nobody checked in to when its window closes is released: `Cancelled`,
  with `ReleasedAt`. A background job looks every 30 seconds. Calendars free the slot through
  the usual `ReservationCancelled`; the owner also gets `ReservationReleased`, a toast saying why.
- **Knowing at booking time.** A reservation keeps whether its resource asked for a check-in
  when it was booked or last moved: a new start, or another resource. Staying longer or ending
  early is the same visit and keeps it. It's released only while that and the resource's own
  setting are both on.
- **One rule, both sides.** `CheckInRules` holds the window and the release condition as
  expressions, used by the check-in, the release job and the queries. Their start conditions are
  exact opposites, and both act under the reservation's row lock, like update and cancel
  (ADR-0013).

**Why:**
- The brief lists QR check-in as a bonus. What it's worth is the rooms held by meetings that
  never happen: without a check-in, nothing tells the booking apart from a real one, so the room
  stays empty and blocked.
- Per resource, because not everything has a door to put a code on: a camera or a car booked for
  the day can't be "checked in" the same way. Off by default, so switching the feature on
  changes nothing until someone asks for it.
- The secret, because resource ids show up in the app's own addresses. With only the id,
  anyone could check in from their desk without going to the room.
- Remembering the setting on the booking, because switching check-in on for a room shouldn't
  release a booking made yesterday by someone who couldn't know. Requiring the resource's
  setting as well lets an Admin switch it off, e.g. when the printed code is gone, and stop
  every release at once.
- Releasing as a cancellation, with `ReleasedAt` beside it, keeps every existing rule, query and
  screen right: a released booking frees its slot exactly like a cancelled one. The timestamp
  is what tells them apart for the owner.
- **Trade-offs:**
  - A printed code proves someone was at the door once, not now: whoever has the link can
    check in from elsewhere until the code is replaced. A screen at the door with a changing
    code would close that, and is out of scope. (ADR-0034 adds that screen.)
  - The release can be up to one job interval late, never early.
  - Only the owner can check in. A colleague who arrives first can't keep the room for them.
    (ADR-0032 lets anyone invited to a meeting check it in.)

## ADR-0031: The whole app in containers: nginx serves the build and passes /api and /hubs to the API

**Decision:**
- `frontend/Dockerfile` builds the production bundle and serves it with nginx
  (`nginxinc/nginx-unprivileged`: not root, port 8080, like the API's image). docker-compose's
  `web` service runs it in a new `app` profile, which starts the API too:
  `docker compose --profile app up -d --build`, then `http://localhost:4200`.
- **One origin.** nginx passes `/api` and `/hubs` to the API container, and serves the app
  for everything else, with `index.html` for its own routes. As with `ng serve`'s proxy, the
  browser makes no cross-origin call, and the session cookie (ADR-0026: `SameSite=Strict`,
  path `/api/auth`) works as it is.
- **The proxy the API trusts.** nginx gets a fixed address on a network of its own (`proxy`,
  172.28.42.0/24), and the API lists that address in `ForwardedHeaders:KnownProxies`
  (ADR-0028). Trusting the whole network would also trust whatever reaches the API's
  published port through Docker's gateway.
- **What goes through it:**
  - SignalR gets WebSocket upgrades and no buffering.
  - Requests may be 8 MB, for photos (ADR-0027).
  - An answer may take 120 s, for the assistant's 90 s limit (ADR-0029).
  - Docker's DNS is asked again every 10 s, so a restarted API is found at its new address.
- **Caching:** hashed scripts, styles and fonts are kept for a year. `index.html`, the
  pictures and the translations are checked again on every visit, so a new release shows at
  once.
- **No secrets in the access log:** it leaves out query strings and the Referer. SignalR sends
  its access token in the query string, and a password reset link carries its token there.
- **Port 4200,** the address `ng serve` uses. The API's Development settings (`App:FrontendUrl`,
  CORS) already name it, so links in emails work in both setups, which can't run at once.

**Why:**
- Burak's Docker work (#18) put the API in a container, but the app still needed Node.js and
  `ng serve`. Now anyone with Docker runs the whole app with one command, which is what a demo
  on someone else's machine needs.
- nginx in front of the API is the setup ADR-0028 was written for, and this runs it for real.
- **Trade-offs:**
  - Like the `api` profile, it's a local stand-in in Development: demo data and no HTTPS. A
    production setup would add TLS at nginx or a load balancer, and not publish the API's port.
  - The fixed subnet can clash with a network already on the machine. The compose file says
    which lines to change.
  - nginx's error log still shows a failing request's query string. Errors are rare, and that
    log isn't kept anywhere.

## ADR-0032: Meetings: people invited to a booking, their answers, and an inbox

**Decision:**
- **Who invites whom.** A booking can have a list of people invited to it
  (`ReservationAttendee`). A Manager may invite their own team's active members, an Admin any
  active account, an Employee nobody. Whoever is added must be able to use the resource
  (ADR-0022), and the room must hold everyone, the organizer included. Someone already on the
  list stays on it after leaving the team; they can only be taken off.
- **The organizer stays the owner.** When an Admin changes a meeting, its owner is still the
  organizer, and the Admin is named as the sender of the messages the change causes.
- **An inbox.** Each user has messages (`InboxMessage`) about the meetings they're invited to:
  invited, moved to a new time or room, cancelled or released, taken off the list. The
  organizer gets one for each answer. A message keeps its own copy of the meeting (room, times,
  note), so it reads the same later, and the app writes the sentence from its kind, in the
  reader's language. `GET /api/inbox` lists the newest 100, read or not, with an unread count
  for the badge.
- **Answers.** Someone invited answers coming or not (`POST /api/reservations/{id}/response`);
  not coming needs a reason. The organizer gets a message either way, the reason in it. A new
  start or another room asks everyone to answer again.
- **Live.** Each message is pushed to its recipient only (`InboxMessageReceived`), once the
  change is committed. An answer reaches every screen showing the meeting as
  `ReservationUpdated`.
- **Check-in (amends ADR-0030).** Anyone invited may check the meeting in at the door; whoever
  gets there first keeps the room for everyone.

**Why:**
- A manager meeting the team in a room picks from a list whom to meet, and those people are
  told. The inbox is where that stays, and where they answer.
- Managers invite their own team because that's whom they manage, and the team already shares
  their resources (ADR-0022). Admins run the office and can invite anyone who may use the room.
  Inviting colleagues at will would turn the inbox into noise.
- A message keeps its own copy of the meeting because the booking changes or goes away: "moved
  to 14:00" must still say so after a second move.
- The words are written by the app, at read time, in the reader's language (ADR-0025), rather
  than by the API in the sender's.
- A reason for not coming goes to the organizer only. The booking, with its list and answers,
  is visible to everyone who may use the resource and sent to their screens; "I'm at the
  doctor's" isn't for them.
- An answer to 10:00 isn't an answer to 14:00, or to another building. A meeting that only
  runs longer is the same meeting, and keeps its answers (ADR-0033).
- **Trade-offs:**
  - Who's invited, and what each answered, is as visible as who booked it. Reasons aren't.
  - The inbox lists the newest 100 messages; older ones stay in the database.
  - A message pushed while its reader is offline isn't pushed again. The inbox and its count
    catch up when they open the app or reconnect.
  - Changing only the note tells nobody.

## ADR-0033: Asked before a booking ends whether it needs longer, and extended with a click

**Decision:**
- **The question.** Ten minutes before a booking under way ends, its owner is asked whether
  they need longer (`ReservationEndingSoon`, to them only): a card in the app, and a desktop
  notification when allowed. A background job looks every 30 seconds
  (`EndingSoon:IntervalSeconds`; `EndingSoon:Enabled: false` switches it off) and asks once
  per end (`EndingSoonSentAt`); a new end, however it comes, is asked about again.
- **The answer is a button.** 15, 30 or 60 minutes more, only those that end before the
  resource's next booking or blocked window (`GET /api/reservations/{id}/extension`).
  `POST /api/reservations/{id}/extend` adds one, for the owner or an Admin, under the same locks
  and booking checks as moving a booking: only the extra time is a new claim on the resource.
- **The same visit.** Only the end moves. The check-in (ADR-0030), the reminder and the answers
  to the invitation (ADR-0032) stay as they are, and everyone invited is told it runs longer
  (`MeetingExtended`).
- **Also from the list.** A booking in progress has **Extend** in My reservations, with the
  same buttons.

**Why:**
- A meeting that runs over is the usual reason to change a booking while it's on, and the edit
  form makes that a hunt for the end field and a new time. A question at the right moment, with
  answers that are known to fit, makes it one click.
- Offering only what fits, rather than letting the click fail, because the next booking is
  usually the reason it can't run on.
- The same checks as a move, because the extra time is a claim like any other: a room switched
  to maintenance or blocked meanwhile can't be taken.
- Not a new visit, because the people in the room are the same: a check-in made at 10:00 still
  holds, and nobody has to answer the invitation again.
- **Trade-offs:**
  - The question can come up to one job interval late, and only to screens that are open: one
    that finds the owner offline isn't repeated. The **Extend** button is always there.
  - Only the owner (or an Admin) can extend, not someone invited.
  - 15, 30 or 60 minutes; anything else goes through the edit form.

## ADR-0034: A door screen with a changing code, and more to do at the door

**Decision:**
- **A door screen.** Those who may edit a resource can set up a screen at its door: a tablet
  that opens `/door/{id}` from a link shown once. The link carries the screen's own token
  after `#`, so it never reaches a server or its logs; the page keeps it and takes it out of
  the address. Only the token's SHA-256 is stored. A new link cuts off the old screen; turning
  the screen off goes back to the printed code.
- **A changing code.** While a screen is set up, only its code checks in: an HMAC-SHA256 of the
  current 30-second step, keyed by the resource's secret, 20 hex digits. The step before is
  still taken, so a code scanned as it changes works. The printed code is refused, saying why.
  (Since ADR-0039 the code is keyed by the screen's own key instead: the printed secret could
  compute it.)
- **What the screen shows.** It asks `GET /api/door/{id}` with its token in a header, without
  a user: the room, today's bookings as times only (a corridor is no place for names), blocked
  windows, and the check-in link with the current code. It asks again just after the code
  changes, at least every 10 seconds.
- **At the door.** The check-in page takes the code out of the address as soon as it's read. A
  booking remembers who checked it in (`CheckedInByUserId`). Its owner can make it run longer
  (ADR-0033) or end it there (`POST /api/reservations/{id}/end`). Someone with nothing to check
  in to sees whether the room is free, and can book 30 or 60 minutes on the spot, checked in at
  once.
- **A warning before the release.** Five minutes before a booking nobody checked in to is
  released, its owner and those invited get `ReservationReleaseWarning`, once
  (`ReleaseWarningSentAt`), from its own job on the check-in schedule. (Since ADR-0040, at the
  start when the grace is shorter than five minutes.)
- **Rate limits.** Checking in: 20 a minute per user. The door screen: 600 a minute per
  address.

**Why:**
- ADR-0030's own trade-off: a printed code proves someone was at the door once. A photo of it,
  or its link, checks in from anywhere until it's replaced. A code that changes every 30 seconds
  is only good to someone standing in front of the screen, for a minute at most.
- The step before is taken so that scanning a code just as it changes isn't refused; anything
  older is a photo.
- The screen proves itself with a token rather than a user's sign-in: nobody should stay signed
  in on a tablet in a corridor, and the token can do nothing but show that one room's screen.
  Keeping only its hash means a copy of the database can't drive a screen.
- Turning a screen on is what switches to changing codes, rather than a separate setting, so
  there's no state where check-in needs a screen nobody set up.
- A booking at the door is checked in at once because the person making it is standing there.
- The warning, because a released booking is a surprise to someone running late; five minutes
  is enough to get to the door.
- The limits: a person scans a few times at most, a script guessing codes far more. A screen
  asks about six times a minute, and a building's screens usually reach the API from one
  address, so theirs leaves room for a hundred.
- **Trade-offs:**
  - A photo of the screen sent to someone elsewhere works for up to a minute.
  - Whoever has the screen's link can show its codes anywhere until it's replaced. The link is
    shown once and meant to be opened on the tablet only.
  - With the screen off or broken, nobody can check in until someone turns it off or replaces
    the link; the warning still goes out, and the release follows as usual.

## ADR-0035: Bookings that repeat

**Decision:**
- **A series is its rule and one booking a date.** `ReservationSeries` keeps the rule it was
  made with: daily or weekly, every 1 to 4, the days of a weekly one, the last day it may come
  back on, and the booker's time zone. Each date is an ordinary `Reservation` with its
  `SeriesId`, booked when the series is.
- **Dates in the booker's time zone.** The first date is the booking asked for; the others keep
  its local time of day and its length, so a meeting at 10:00 stays at 10:00 when the clocks
  change. Weeks are counted from Monday: "every 2 weeks" is the first date's week, the one two
  weeks on, and so on. A time the clocks skip moves to the first minute after it. A weekly series
  has to include the day it starts on.
- **As far ahead as any booking.** The last date may start at most 180 days ahead (issue #41),
  so a daily series has at most 181 dates. At least two dates, and none running into the next.
- **Seen first, then booked at once.** `GET /api/reservations/series/preview` lists the dates
  and which are free. `POST /api/reservations/series` checks every date under the resource's
  lock and books them in one transaction. One date that isn't free stops the lot (409,
  `series-conflicts`) unless the booker asked to skip such dates (`skipConflicts`); the answer
  lists the dates booked and those skipped, and why.
- **One date at a time, or the rest.** A date is moved or cancelled like any booking. `POST
  /api/reservations/{id}/cancel-following` cancels a date and every later one still to come.
  The rule is never edited: a different rhythm is a new series.
- **One message for a series.** Those invited get one `SeriesInvitation` for every date, not one
  each, and answer once with `following` for the date and every later one they're invited to
  (`SeriesAccepted`, `SeriesDeclined`). Cancelling the rest sends one `SeriesCancelled`. A change
  to one date tells them about that date, as before.
- **The form.** A new booking has a Repeat choice: every day, every weekday, every week, every
  2 weeks, with the days of a weekly one and a last day four weeks on to begin with. The form
  asks for the dates as it changes, lists them, those that aren't free with why, and its button
  books them all or the free ones.

**Why:**
- Every date being a booking of its own means everything a booking does - the overlap rule and
  its constraint, reminders, check-in and release, running over, meetings, the calendar and the
  live events - works for each date unchanged. Expanding a rule on every read would have had to
  teach all of them about series, and the overlap check couldn't lean on the database any more.
- One transaction under the resource's lock is the guarantee a single booking has (ADR-0011):
  what was checked is what's booked, and nothing half-booked is left behind.
- Refusing the lot by default means nothing is booked that the booker didn't see; the preview
  makes skipping a choice made knowingly, and booking checks again, so a date taken meanwhile
  is never double-booked.
- Local times in the booker's zone are what a person means by "every Monday at 10:00"; fixed
  UTC times would drift by an hour twice a year in zones with summer time.
- The 180-day limit already stops one person holding a room for years; a series is held to it,
  so it needs no other cap.
- One message per series: a weekly meeting for six months would otherwise fill an inbox with 26
  invitations to answer one by one.
- **Trade-offs:**
  - A daily series for six months is 181 rows and 181 live events at once. Fine for an office;
    the events let open calendars patch themselves without a reload.
  - Changing a series' time means cancelling the rest and booking a new one; an "edit every
    date" would have to re-check them all, and say which failed.
  - A date moved on its own stays in its series; "cancel the rest" cancels it too.
  - A resource with a long series ahead can't go into maintenance until those dates are
    cancelled (issue #41), as with single bookings.

## ADR-0036: The assistant can run on Gemini as well as Claude

**Decision:**
- `Assistant:Provider` chooses the model behind the assistant: `Anthropic` (Claude, the
  default) or `Gemini`. `Assistant:ApiKey` is that provider's key; an unknown provider stops the
  API at startup.
- `GeminiAssistantModel` is a second `IAssistantModel`, beside `ClaudeAssistantModel`. It calls
  Gemini's REST `generateContent` endpoint through `HttpClient`, with no SDK. The key goes in the
  `x-goog-api-key` header, never in the address.
- The same loop as Claude's: the toolbox's tools become function declarations, their input as a
  plain JSON Schema (`parametersJsonSchema`); a function call is run as the signed-in user and its
  result goes back as a `functionResponse`, after the model's turn exactly as it came, thought
  signatures and all. The last round sets the function-calling mode to `NONE`, so it answers
  with what it has. One time limit for the whole answer.
- The same answers to the user: a refused key, a rate limit, a failure or no connection is the
  503 "can't answer right now"; an answer withheld on safety grounds is the refusal message. The
  log has Google's own reason, which never contains the key.
- `Assistant:GeminiModel` is `gemini-flash-latest`, the alias of Google's newest Flash model.
  `Effort` is Claude's alone; Gemini thinks as its model does by default.

**Why:**
- The team asked for Gemini too, and Google AI Studio gives a key with a free tier, so the
  assistant can be shown without a paid Anthropic account.
- The Application never knew the provider (`IAssistantModel`, ADR-0029): the instructions, the
  tools, what they may do and how a proposal becomes a booking stay the same whichever model
  answers, and so do the tests of those.
- REST rather than an SDK: one endpoint and a handful of fields, no new dependency to keep up to
  date, and a stand-in API in the tests is just an `HttpMessageHandler`.
- `parametersJsonSchema` takes the tools' schemas as they are; the older `parameters` field takes
  only an OpenAPI subset, without `format: uuid` for one.
- Echoing the model's turn unchanged is what thinking models require between function calls;
  rebuilding it would drop the signatures.
- **Trade-offs:**
  - Claude caches the instructions between rounds; this Gemini call doesn't use context caching,
    so a long conversation costs a little more per round.
  - The `-latest` alias moves when Google ships a new Flash; pin a model in
    `Assistant:GeminiModel` to keep the same one.
  - The provider is one setting for the whole server; nobody chooses per question.

## ADR-0037: Problems reported at the door, followed up by those who manage the resource

**Decision:**
- **Anyone who may use a resource reports a problem with it**, booked or not, usually from the
  page its QR code opens (ADR-0030). A report has a category (a screen or TV, the projector, the
  camera and sound, air conditioning, cleaning, furniture, other), what's wrong in up to 500
  characters, whether it makes the resource unusable, and optionally a photo taken there. It's
  an `IssueReport`, with its photo in `IssueReportPhotos`. (Since ADR-0050, also at the door
  without an account: the report then names nobody.)
- **One report per problem.** While a report of a category is unresolved on a resource, another
  of the same category is refused with 409 `issue-already-reported`. The page at the door lists
  the open ones, so people see them first. `Other` is never a duplicate. Reporting takes the
  resource's lock (ADR-0011), so two people reporting at once can't both get in.
- **Those who manage the resource follow it up:** the Admins, and the Manager of the one team it
  belongs to (ADR-0022).
  - They see its reports (`GET /api/issues`), unresolved first and the blocking ones first
    among those.
  - They hear about new reports and changes live, as `IssueReported` and `IssueUpdated`. The
    events go to the Admins' group and that Manager only.
  - They move a report from `Open` to `InProgress` and back, and then to `Resolved`, which is
    final.
- **The reporter hears back.** Resolving sends them an `IssueResolved` inbox message (ADR-0032)
  with the category and their own words, unless they resolved it themselves.
- **Photos as for a resource (ADR-0027):** JPEG, PNG or WebP, recognised by their bytes, at most
  2 MB, made smaller in the browser first, and kept in the database. Anyone who may use the
  resource may fetch one.
- **A limit:** 10 reports a minute per user.

**Why:**
- The person standing in the room is the first to see a broken TV, and the QR code at the door
  already brings them into the app. Reporting there takes seconds and needs nothing installed.
- Categories as well as free text, because they decide "the same problem" without anyone
  reading. `Other` is too broad for that.
- A duplicate is refused rather than kept: ten reports of one dead projector are noise for
  whoever fixes it, and listing the open ones at the door tells people it's known.
- The same people as for the resource itself: whoever may switch it off or block it should hear
  that it's broken. An Employee only uses it.
- Resolved is final because reopening could break the one-report rule: the same problem may have
  been reported again meanwhile. A problem that comes back is a new report, with its own time.
- The inbox, because a reporter is rarely still at the door when the fix comes, and a toast
  would be missed.
- Photos in the database, for ADR-0027's reasons: a few hundred KB each, and nothing else to back
  up or deploy.
- **Trade-offs:**
  - Who reported a problem is visible to anyone who may use the resource, like who booked it.
  - A report can't be edited or withdrawn. Whoever manages the resource resolves a mistaken one.
  - Beyond the live events and the list, nothing tells those who manage the resource: no email.
  - Reports are never deleted, and the list isn't paged yet.

## ADR-0038: A block can take its window's bookings with it, when asked

**Decision** (amends ADR-0015):
- **Asked, not by default.** `POST /api/resource-blocks` takes `cancelReservations`. Without
  it nothing changes: `Confirmed` bookings in the window are a 409 that counts them. With it,
  they're cancelled in the same transaction that creates the block. A booking under way is in
  the window too.
- **Everyone on them hears why.** The owner and everyone invited get a `CancelledByBlock`
  inbox message with the block's reason, from whoever blocked it (they themselves get none).
  Every screen that may see the resource hears `ReservationCancelled`.
- **The usual lock order.** The bookings are locked first, one by one in id order, and then
  the resource, the order every other handler takes them in (ADR-0011). A booking made after
  they were found but before the resource was locked means looking again, three times at most,
  and then the usual 409.
- **What uses it.** Taking a resource out of use from a problem report (ADR-0037) closes it
  until a chosen time. The screen says how many bookings that cancels before it asks.

**Why:**
- A broken room has to close now, and ADR-0015's "cancel or move them first" means one booking
  at a time, just when time matters. The person closing it still decides: the count is shown
  first, and nothing is cancelled without the flag.
- A block rather than the `Maintenance` status: a block ends by itself, so a TV that's fixed by
  tomorrow doesn't cancel next month's bookings, and the calendar shows the window with its
  reason. `Maintenance` still waits for the bookings to go (ADR-0024).
- The inbox, because cancelling someone else's booking otherwise only reaches the screens they
  have open, and they may not have any.
- Locking in the usual order means someone moving one of those bookings at that moment
  can't end up waiting on this handler while it waits on them.
- **Trade-offs:**
  - A meeting under way is cancelled as a whole, not cut short, so its time so far drops out of
    the usage report.
  - Lifting the block early (deleting it) doesn't bring the cancelled bookings back.

## ADR-0039: A door screen's codes are keyed by the screen, not by the printed code

**Decision** (supersedes the key in ADR-0034, fixes issue #101):
- A door screen's changing code is an HMAC-SHA256 of the 30-second step keyed by the screen's
  own key: `DoorScreenTokenHash`, the SHA-256 of the token in its link. It used to be keyed by
  `CheckInCode`, the secret in the printed QR code.
- Nothing else changes: the same 20 hex digits, the step before still taken, the printed code
  refused while a screen is set up and taken again once it's turned off.

**Why:**
- The printed secret is in every printed code and in its link, and `GET
  /api/resources/{id}/check-in-code` returns it. With the algorithm in the repo, anyone holding
  the printed code - a photo taken before the screen went up, or the copied link - could compute
  every screen code offline, and check in from anywhere for as long as the screen stayed. That's
  exactly what the screen exists to stop.
- The token's hash is never shown by any endpoint, and a new screen link makes a new one, so
  the old screen's codes stop with it. Using it needs no migration and no new secret to look
  after.
- **Trade-offs:**
  - A copy of the database can compute the codes, as it could compute the printed one's before.
  - A new screen link changes every code at once; the screen picks up the new one on its next
    look, within ten seconds.

## ADR-0040: The check-in grace is a setting

**Decision** (amends ADR-0030 and ADR-0034):
- How long after its start a booking that needs a check-in waits for one before it's released
  is `CheckIn:GraceMinutes`: 15 by default, from 1 to 60. The API doesn't start with another
  value. The window still opens 10 minutes before the start.
- The warning before a release comes five minutes before it, as before, or at the start when
  the grace is shorter: never before the booking begins.
- `GET /api/check-in/times`, open to anyone, gives the window's lengths. The app reads them once
  as it starts, before the first page, and says and counts every check-in time with them: the
  check-in page, the forms, the poster, the guide, the door screen, the reminders. The
  assistant's instructions take the grace too.

**Why:**
- The demo in the plan shows a booking nobody checked in to being released. With a quarter of
  an hour that's a long wait in front of an audience; with one minute it happens while they
  watch.
- Everything that states or counts the window takes it from the one setting, so the app can't
  say "15 minutes" while the API releases after one. An office may want another grace too.
- Only the grace moves: the opening stays fixed, and nothing asked for it.
- **Trade-offs:**
  - A booking's window isn't stored, so a new grace applies at once to every booking, the ones
    waiting at the door too.
  - The English texts that give the grace in minutes say "1 minutes" for a one-minute grace.
    It's a demo setting, and the Turkish texts have no plural there.
  - The app waits for the times as it starts, five seconds at most, and goes on with the
    defaults without them.

## ADR-0041: Floor plans: an uploaded picture per floor, with the resources placed on it

**Decision:**
- A floor is a resource `Location`: the resources that share one are on it. An Admin uploads a
  picture of it, kept in `FloorPlans` keyed by the location, under a photo's rules (ADR-0027):
  JPEG, PNG or WebP, 2 MB, 4096 pixels a side. No SVG, which can carry script.
- A resource's place is `MapX` and `MapY`: fractions of the picture's width and height, 0 to 1.
  Whoever may edit the resource places it (ADR-0022), and a new location clears it.
- People see the floors of the resources they may use, and only those resources on them. An
  Admin also sees plans no resource is on yet, to replace or remove them.
- The app colours each resource for a chosen time from that day's bookings and blocks, as the
  check-in page does: free, in use, out of use. Choosing one opens the booking form for it from
  the first quarter hour it's free, past bookings back to back, for an hour or until it's taken
  again. A time gone by offers nothing to book. Bookings arrive live over the hub.

**Why:**
- The demo shows where everything is and what's free, at a glance. A picture of the real office
  says more than one drawn for it, and any drawing tool exports one.
- Fractions survive a plan exported at another size. A plan with another layout needs its
  resources moved, which an Admin does on the page.
- Keyed by location, a floor is nothing new to manage: give a resource another location and it
  moves floors, its place cleared.
- **Trade-offs:**
  - Locations are free text, so "5th Floor" and "5th floor" are two floors. Admins keep them
    the same.
  - A plan whose resources have all moved stays until an Admin removes it; only Admins see it.
  - The colours come from bookings, not from who is in the room; a check-in is as near as the
    app gets to that.

## ADR-0042: A booking that can't be made says what would do instead

**Decision:**
- When a new booking, or a booking being moved, clashes with another booking or a block, or its
  resource is out of use, the 409 is the same as before, with `suggestions`: the nearest free
  times of the same length on that resource, and other resources of its type free at that time.
- Times: within a day either side, one in each free stretch, as near the time asked for as the
  stretch allows, on the quarter hour; never in the past or beyond the booking horizon. The
  three nearest, shown earliest first.
- Resources: the caller's to book, `Active`, free and unblocked for the whole time, and big
  enough for the owner and everyone invited. Those at least as big as the one asked for first,
  the smallest first; then the smaller ones, the biggest first. Four at most.
- A MediatR pipeline behaviour wraps the create and update commands: when their handler throws
  one of those conflicts, it works the suggestions out and throws the conflict again with
  them. The handlers don't change.
- The booking form offers them under the message: choosing a time or a resource fills it in,
  and booking stays a click the user makes.

**Why:**
- "Taken" was a dead end: the user had to search again with the times they'd just given. The
  room usually frees up within the hour, or a room like it is free; the API already knows both.
- In the 409 itself, not a second request: the clash and what to do about it arrive together,
  and a client that ignores them is no worse off.
- After the handler, outside its locks: suggestions are a read, like the availability search,
  and holding a resource's lock while looking at others would slow everyone booking it.
- One free time per stretch, nearest first, rather than every free quarter hour: "an hour
  earlier, or right after" is a choice; ten starts a quarter hour apart are a list.
- **Trade-offs:**
  - A suggestion is worked out after the clash and holds nothing, so it can be gone by the time
    it's chosen; then the next try says so, with new suggestions.
  - A day either side and the quarter hour: a resource booked solid for days offers no time, and
    a free stretch that doesn't start on a quarter hour loses its first minutes.
  - Night hours count like any other: there are no opening hours yet, so a room booked all day
    may be offered for the evening.
  - An Admin moving someone else's booking is offered rooms by the Admin's access, so one the
    owner may not use can be among them; choosing it is refused as before (ADR-0022).

## ADR-0043: A waiting list for a taken time, booked for whoever waited first

**Decision:**
- Someone whose booking clashes with another booking or a block can wait for that time on that
  resource instead (`WaitlistEntries`). Not for a time that's free, one they booked themselves,
  one they already wait for, nor more than 10 at once.
- When the time frees up, it's **booked** for whoever joined first, if they may still use the
  resource and it's in use, and their inbox gets a `WaitlistBooked` message; their entry goes.
  Then the next in line, in the order people joined: entries for times that don't overlap can
  all be booked from one freed time, and one still partly taken waits on. Someone who may no
  longer use the resource, or a time begun with under 15 minutes left, drops out.
- What frees a time is read from the saves themselves: an EF Core interceptor notes a
  `Confirmed` booking cancelled (released for want of a check-in too), ended early, shortened,
  moved in time or to another resource, and a block deleted. A MediatR pipeline behaviour
  publishes what a request freed as `SlotFreed` notifications once it has succeeded, and their
  handler books, each entry under the resource's lock like any booking (ADR-0011).
- The booking form offers it under a clash, next to the suggestions (ADR-0042); My reservations
  lists what the user waits for, their place in line, and a way to leave it.

**Why:**
- "Taken" with nothing to do but check again later loses the room to whoever happens to look
  at the right moment. A line puts the person who asked first first.
- Booked, not just told: a message to the first in line would hold nothing - whoever reads
  theirs first would win, and with nobody holding it, the order would mean little. A booking
  they didn't need is a cancel away, and that offers it to the next.
- Read from the saves: cancelling, a release, ending early, moving and lifting a block are six
  handlers, and the next way to free a time would be a seventh; a change of the data can't be
  missed. Offered after the request, outside its transactions and locks, and only once it has
  succeeded, so nothing is offered that rolled back, and a failure here never fails what freed
  the time.
- **Trade-offs:**
  - Someone can get a booking at a moment they no longer look at their inbox; it has the usual
    reminder, and a room asking for a check-in releases it again if they don't come.
  - An entry is for one resource and one time: someone happy with any room like it joins the
    line of each, up to 10.
  - Bulk updates that bypass EF's change tracking wouldn't be seen; none touches bookings or
    blocks.

## ADR-0044: The usage report shows when it's busiest and how often bookings go unused

**Decision:**
- The usage report (ADR-0017) adds a heat map: for each weekday and local hour, the booked
  share of the listed resources' time in that hour, over the days of that weekday in the range.
- It counts **no-shows** - bookings released because nobody checked in (ADR-0030) - and the
  check-ins made, per resource, per type and in total. The no-show rate is no-shows over both:
  of the visits a check-in was asked for, the share nobody came to. The cancellation rate is
  bookings cancelled by someone, a release not counted, over every booking in the range.
- Both are computed like the rest of the report: in memory, from the bookings overlapping the
  range, in the caller's time zone (`WeekHeatMap`).
- The page draws the heat map as a table: a column per weekday, a row per hour of the office
  day plus any other hour something was booked in, each cell coloured in one of four steps of
  the primary colour and showing its share. No chart library. The report's tables take the
  keyboard's focus while they're wider than the page and scroll sideways (`appScrollRegion`).

**Why:**
- "When is it full" is what decides a new room or a desk policy, and a week of hours answers it
  at a glance; the totals and per-resource figures don't.
- A released booking is a room held and not used - the cost of a no-show - while one cancelled
  in time freed the room for others. They are counted apart so neither hides the other.
- The plan suggested grouping in the database. The heat map's hours are local, with days of 23
  or 25 hours, as the office hours already were; the report keeps one way of computing, bounded
  by its 92 days (ADR-0017).
- A table is the chart here: it reads the same in both themes and both languages, screen
  readers get each cell with its weekday and hour, and axe checks it like any page. One hue in
  four steps, with the share in each cell, so colour is never the only cue; the cell text keeps
  4.5:1 on every step in both themes (on the darkest, the text made for the primary colour). A
  chart library would add weight for one picture and still need a table beside it.
- **Trade-offs:**
  - The lightest step is close to the page's own colour, so a nearly empty hour stands out by
    its number more than by its shade.
  - On the day the clocks change, the missing hour reads 0 and the doubled one counts once.
  - Blocks and maintenance aren't taken off the available time, as before (ADR-0017).

## ADR-0045: The usage report downloads as an Excel workbook, written with ClosedXML

**Decision:**
- `GET /api/reports/usage/workbook` (Admin) takes the report's parameters and returns the same
  report as an `.xlsx`: a summary sheet, then resources, types and people as Excel tables, and
  the heat map by hour with a white-to-primary colour scale.
- Written with ClosedXML (MIT; its dependencies are MIT and Apache-2.0) behind
  `IUsageReportWorkbook`, implemented in Infrastructure. The controller asks for the report, then
  has it written: the report's numbers are computed once, in one place.
- In the request's language, like the API's messages (ADR-0023): sheet names, headings,
  statuses, the seeded kinds of resources and the weekdays. Numbers stay numbers, with Excel
  formats (percent, hours, dates), never pre-formatted text.
- The page's "Download as Excel" fetches it with the token and saves it (`saveFile`).

**Why:**
- Management reads reports in Excel: to filter, add columns, chart and mail them. A file of the
  same numbers beats retyping them from the screen.
- The server writes it because the report is the server's: one computation for the page and
  the file, so they can't disagree. ClosedXML, as the plan suggested: a real `.xlsx` with
  tables and conditional formats, not CSV, which Excel opens with the wrong encoding and
  separators in Turkish locales.
- Real numbers, not text: a "25%" typed as text can't be summed or sorted; 0.25 shown as 25% can.
- **Trade-offs:**
  - Column widths are set, not measured: measuring would need fonts on the server, which a
    container may not have.
  - A kind of resource added after the seed keeps the name it was given, as on screen.
  - The file is built in memory; the 92-day range bounds it to a few hundred rows.

## ADR-0046: Booking rules an Admin sets for each kind of resource, applied by one policy

**Decision:**
- Each kind of resource has four rules an Admin sets, each unset until then: the longest a
  booking may last (`MaxDurationMinutes`), how many days after today it may start
  (`MaxDaysAhead`, 0 for today only), how many bookings of the kind one person may have that
  haven't ended (`MaxActivePerUser`), and whether it's booked within office hours only
  (`OfficeHoursOnly`). So cars can go out for days while desks keep to the office day.
- The office's hours are one row, `OfficeHours`: an IANA time zone, opening and closing times on
  the quarter hour, and the workdays. Seeded as Mon-Fri 09:00-18:00 in Europe/Istanbul, the
  hours the usage report always measured against, which it now reads from there too.
- A kind's rules only tighten the rules for every booking (`ReservationRules`: 31 days long, 180
  days ahead); the API refuses values past them. Those stay in the requests' validators, as before.
- One service applies them all: `BookingPolicy`, over the pure `BookingLimits` and `OfficeWeek`.
  Every way of booking asks it: booking, moving, a series, running longer, the waiting list. So
  does everything that offers times: the search leaves out kinds whose rules don't allow the
  window, suggestions keep to them, and the extension options stop at closing time.
- A time the rules don't allow is a 400 on the field it's about, saying which rule and the value.
  A booking over the per-person limit is a 409 with the code `booking-limit`. In a series, the
  first date is checked as any booking and the last date for how far ahead it is; dates outside
  office hours (a daily series' weekend) are skipped like dates that aren't free, with the code
  `outside-office-hours`.
- The rules hold for Admins too, and for whoever owns the booking: an Admin moving someone's
  booking counts the owner's bookings. Moving a booking within its kind isn't one more.
- Bookings already made stay as they are when the rules change. Shortening one claims nothing new
  and is always allowed; moving it later is a new claim and keeps to the rules then.
- The per-person limit counts under a lock on the person, a transaction-scoped PostgreSQL advisory
  lock, taken after the resource's (ADR-0011). Without it, two bookings of two desks at once each
  hold their own desk's lock and both pass.
- `GET /api/booking-rules` for anyone signed in, so the booking form can show the rules and keep
  to them before it asks. `PUT /api/booking-rules/office-hours` and
  `PUT /api/booking-rules/types/{id}` for Admins.
- The page is **Admin → Booking rules** (`/admin/rules`). The booking form shows the chosen
  kind's rules under the resource and checks the time against them before it asks, in the
  office's time zone; the limit per person it leaves to the API, which knows the bookings.

**Why:**
- The plan's day 5: rules an Admin can change without a release, varying by kind, collected in
  one policy service rather than spread over the handlers. Before, the limits were constants that
  each handler checked where it needed them; now the handlers ask the policy, and the constants
  are its outer bounds and the validators' first check.
- Per kind, not per resource: the rules follow what a thing is (a car, a desk), and a handful of
  kinds is something an Admin can keep track of.
- Days ahead in the office's calendar ("up to 7 days ahead" is up to the end of that day), not in
  24-hour steps: that's how people say it, and it lets "today only" be a rule.
- A 400 for times and a 409 for the limit: the first is about the request, which the form can fix
  before sending; the second is about the person's other bookings, which change.
- **Trade-offs:**
  - The office has one time zone and one set of hours. An office in two time zones would need
    hours per location.
  - Office hours are checked per booking: a booking can't run overnight on an office-hours-only
    kind, even from one workday to the next.
  - The waiting list skips someone the rules no longer allow (the limit reached, the rules
    changed) and offers to the next in line; their entry waits on.
  - The usage report reads the office's hours in the time zone it's asked for, as it reads its days.

## ADR-0047: Demo data with Bogus, written past the API, on `--demo-data`

**Decision:**
- `--demo-data` on the API's command line runs `DemoDataSeeder` once and stops: on top of the
  Development seed, two more managers and fifteen people in three teams, 21 more resources on
  three floors with their plans (drawn as SVG, kept as PNG, embedded in Infrastructure), six
  weeks of bookings behind and two ahead, two weekly series, five problem reports, a blocked
  window and two people waiting.
- The bookings follow how an office books: busiest mid-week, mornings and early afternoons;
  people at desks near their team; meetings of half an hour to two hours with their team
  invited; fewer bookings further ahead. Of what's over, about one in five check-ins never
  happened and was released, and about one in fourteen bookings was cancelled.
- Random with Bogus (MIT), Turkish names from its `tr` locale, from a fixed seed: the same
  office on every run, relative to the day. `DemoBookings` works it out in memory, so its
  rules are tested without a database: no two Confirmed bookings of a resource overlap, a
  team's resource is its team's, check-ins and releases match the times, what's to come is
  neither.
- Written straight to the database, in one transaction, past the API's checks: the API won't
  take a booking in the past, and six weeks of them is the point. So Development only, and
  only into a database with no bookings yet.
- Meeting Room A and the Board Room are kept free from now on, for the walkthrough in
  docs/DEMO_UI.md, which books them at times of its own.

**Why:**
- The plan's day 10, "seed realistic demo data with Bogus". A fresh database shows an empty
  report, empty floors and empty calendars: nothing to point at in a demo of reports, the floor
  plan or no-shows.
- A command-line switch rather than part of the Development seed: the browser and integration
  tests start from the plain seed, and random bookings would collide with theirs.
- In the codebase rather than the SQL and Node scripts of the demo kit (`Documents\SmartDesk-demo`):
  it keeps up with the model, its rules are tested, and anyone with a clone can run it.
- **Trade-offs:**
  - Past the API, the rules are the generator's to keep; its tests check the ones the database
    doesn't (the exclusion constraint catches overlaps on PostgreSQL).
  - Bogus ships in the Infrastructure assembly, used in Development only.
  - The plans are pictures of an imagined office; a real one uploads its own (ADR-0041).

## ADR-0048: Bookings in a calendar app: an .ics file per booking, and a personal link to subscribe to

**Decision:**
- `GET /api/reservations/{id}/calendar` gives a booking as an iCalendar file, for its owner or
  someone invited to it: opening it adds the event to a calendar app. My reservations has "Add to
  calendar" on what's upcoming and on invitations.
- One personal calendar link per person. `POST /api/calendar/subscription` makes it and shows it
  once; a new one replaces it, and `DELETE` removes it. Outlook, Google Calendar or a phone's
  calendar subscribes to it and fetches `GET /api/calendar/feed.ics?token=…` again by itself:
  the owner's bookings and the meetings they're invited to and haven't declined, from 90 days ago
  on. Cancelled ones are left out, so they go from the calendar at its next fetch. The profile
  has a card to make, replace and remove it, with "Open in my calendar app" (`webcal://`) and
  "Add to Google Calendar".
- Written with Ical.Net (MIT; its NodaTime dependency is Apache-2.0) behind `ICalendarWriter`,
  implemented in Infrastructure, like the Excel workbook (ADR-0045): the handlers decide what goes
  in, in the reader's words, and the writer only writes it. Times are UTC, and each booking keeps
  one `UID`, `{id}@smartdesk`, so a moved booking updates its event rather than adding another.
- The link's token is 32 random bytes as hex, kept only as its SHA-256
  (`CalendarFeeds.TokenHash`), like a door screen's (ADR-0034). A calendar app can't sign in, so
  the feed is anonymous: the token goes in the query string, the feed is limited by address
  (`RateLimiting:CalendarFeedPermitsPerMinute`, 120), and a wrong token, a removed link and a
  disabled account all get the same 404. The feed is written in its owner's saved language, as
  their emails are (ADR-0023), and notes when it was last fetched, which the profile shows.

**Why:**
- The plan's backup pool: ".ics download and a personal calendar link to subscribe to in
  Outlook/Google Calendar (Ical.Net)". People live in their own calendar, and a booking that's
  only in SmartDesk is easily forgotten.
- A subscription rather than invitations by email: no mail server needed, and the calendar
  follows every change - moved, cancelled, released for want of a check-in - at its next fetch,
  without SmartDesk sending anything. The file is for one booking, for those who'd rather not
  subscribe.
- Ical.Net, as the plan suggested: line folding, escaping and the formats of RFC 5545 are easy
  to get almost right by hand, and calendar apps are strict about the rest.
- Hashed and replaceable: a calendar link is read by servers outside SmartDesk and stays in
  calendar settings for years. When one gets out, a new link is the fix, and a copy of the
  database gives none away.
- In the query string rather than the path: the container's nginx leaves queries out of its log
  (ADR-0031), so the token isn't written to disk.
- **Trade-offs:**
  - The link is shown once. Losing it means making a new one and subscribing again.
  - A calendar app fetches at its own pace - Google's every few hours, whatever the feed
    suggests - so changes show up then, not live.
  - Google Calendar and Outlook on the web fetch from their own servers, so for them the app has
    to be on the internet; on `localhost` only a calendar app on the same computer can
    subscribe, and the profile says so.
  - The calendar is read-only: answering an invitation or moving a booking is done in SmartDesk.
  - Anyone who has the link sees what its owner booked, where and when, and who is invited, as
    the app's own calendar shows them, without signing in.

## ADR-0049: Bookings that wait for an Admin's approval, for the kinds that ask for it

**Decision:**
- A kind of resource can ask for an Admin's approval (`ResourceType.RequiresApproval`), set on
  the booking rules page beside its other rules (ADR-0046). A booking of it that anyone but an
  Admin makes is `Pending`: asked for, not booked yet. An Admin approves it, and it's
  `Confirmed`; or declines it, saying why; or nobody decides before its start, and it lapses -
  `Cancelled` either way. Every active Admin's inbox says when one is asked for, and whoever
  asked hears each outcome.
- `Pending` is a status of its own, not a flag on a `Confirmed` booking. It holds its time as a
  booking does: the one overlap rule (`ReservationOverlap.TakenOverlapping`) and the database's
  exclusion constraint cover it, so two people can't ask for the same car at the same time, and
  an approval never meets a clash. Everything that's for a booking only - the reminder, the
  check-in and the release, the ending-soon notice, "in use now", the usage report - keeps to
  `Confirmed` as it did, and so leaves a request out without each being told.
- The decision is kept on the booking: `RequestedAt`, `DecidedAt`, `DecidedByUserId`,
  `DeclineReason`. An owner who moves it to another time to come, or to another resource of such
  a kind, asks again; shortening it, a new note, an Admin's move, and running on once it's under
  way don't. A request can't start right away, as an Admin needs the time; nor repeat, so each
  date is asked for on its own; nor join a waiting list, as a freed time would be booked with
  nobody there to approve it; nor run longer or end early while it waits.
- `/api/approvals` (Admin) lists what waits, the soonest first, and approves or declines it.
  `LapseUnapprovedRequestsCommand` lets the undecided go every 30 seconds (`ApprovalLapseWorker`),
  as the release of no-shows does (ADR-0030).

**Why:**
- The plan's backup pool: "approval flow: on resources like a company car, a booking is Pending
  first, active once an admin approves it". A car is costly and shared by the whole company:
  someone says yes before it's taken.
- Holding the time: the other way - many requests for one time, the rest turned down when one
  is approved - leaves an Admin choosing between people and everyone guessing until then. The
  first to ask is first in line, as for any booking, and the Admin says yes or no to one.
- A status of its own, so what isn't a booking can't be reminded, released or counted as one by
  accident: only the places that ask whether a time is taken had to learn about it, and they
  ask the one overlap rule.
- Lapsing at the start: a request nobody answered can't be used then anyway, and kept, it would
  hold the car for nothing.
- **Trade-offs:**
  - Admins decide, for every resource; a manager doesn't approve for their team's own
    (ADR-0022).
  - No series and no waiting list for such kinds, so every request is one date an Admin can
    weigh. An Admin's own bookings of them, a series too, need no approval.
  - A request moved after its approval waits again, even for an hour's change.
  - Asking for approval from a kind's next bookings on leaves those already made as they are.

## ADR-0050: The page a QR code opens works without an account

**Decision:**
- A phone nobody is signed in on opens the page a resource's QR code leads to
  (`/check-in/{id}?code=…`, ADR-0030) without being sent to sign in. While nobody is signed in,
  the router matches a page of its own for that address (`visitorGuard`, a `canMatch`), and the
  signed-in page in the shell otherwise, so the codes already printed keep working as they are.
- The code is what lets the page in. `GET /api/door/{id}/visit` takes it in a header
  (`X-Check-In-Code`), as the door screen takes its token (ADR-0034): the printed code, or with
  a door screen set up, one of its codes of the last half hour (`CheckInRules.SeenAtTheDoor`).
  It answers with what the door screen shows - the room, the rest of today's bookings as times,
  its blocked windows - with what the room has, and with the problems reported there and not
  fixed yet, but not who reported them. A wrong code is the same 404 as no resource.
- Whoever is there checks in the booking waiting for it, whoever's it is:
  `POST /api/door/{id}/check-in` with the code. It's the signed-in check-in (`CheckInCommand`,
  with `AtTheDoor`) under the same rules: the window, the reservation's lock, and with a door
  screen, its current code only (ADR-0034). A code that doesn't open the page is the same 404
  here, before anything that would name the room. The booking remembers nobody as having checked
  it in, and the answer is its times.
- A problem is reported the same way, `POST /api/door/{id}/issues` with the code in the form
  (`CreateIssueReportCommand`, with `DoorCode`). Its `ReportedByUserId` is then null: the list
  says it was reported at the door without an account, and nobody is told when it's fixed.
- Booking on the spot, running longer or ending a booking, and anything about one's own bookings
  still take an account. The page links to sign in, and signing in comes back to the same page,
  signed in.
- Without a user, the limits go by address: the page's own
  (`RateLimiting:DoorPagePermitsPerMinute`, 300 a minute), and checking in and reporting a
  problem, which go by the user when there is one, and by address when there isn't.

**Why:**
- The user: scanning the code asked for an email; the page should be open to everyone, with no
  account. A sign-in between the door and the check-in is where people give up, and the room is
  then released while they're in it.
- The code shows its holder was at the door, and that is what a check-in is for: the room is in
  use. Who it is matters less than that someone is there; with a door screen, someone is there
  now.
- The same handlers with a flag rather than new ones, so the window, the locks, the duplicate
  rule and the notices stay one each.
- Times only, and no reporter's name, as on the door screen: anyone with a photo of a code can
  open the page.
- **Trade-offs:**
  - Without a door screen, a photo of the printed code checks in from anywhere, and now anyone's
    booking, not only the owner's own: ADR-0030's trade-off, wider. A door screen closes it.
  - A booking checked in at the door without an account names nobody, so its owner can't tell
    who kept it.
  - Anyone with the code can report a problem: the duplicate rule and the limit by address keep
    that down, and one reported without an account can't be asked about.
  - The page asks again every 30 seconds while it's open. A door screen's code stops opening it
    after half an hour; the page then says it may be out of date.

## ADR-0051: Favourite resources, kept with the account

**Decision:**
- Anyone signed in can star the resources they book often. A star is a `FavoriteResource` row,
  keyed by the account and the resource, with no foreign key across the Identity boundary, and
  deleted with its resource. `GET /api/favorites` lists the caller's own by name,
  `PUT /api/favorites/{resourceId}` stars one and `DELETE` unstars it; both are idempotent.
- Only what the caller may use is starred or listed (ADR-0022): another team's resource is 404,
  and a favourite that moves to another team stays starred, out of sight until it's theirs to
  use again. One switched off or in maintenance is still listed, with its status, so it says why
  it can't be booked rather than going missing. At most `FavoriteRules.MaxPerUser` (30), then
  409 `favorites-full`.
- The app keeps them in one `FavoriteService`, loaded once for whoever is signed in. A star
  changes on screen at once and the API is told after; one it refuses goes back.
- They show where booking starts: a star on every row of **Resources**, with **Favourites only**
  among its filters; on the dashboard, **Your favourites**, each with what it's doing now -
  free until when, in use until when, or not available - and **Book**; and in the booking form,
  first among the resources, in a group of their own.

**Why:**
- The plan's backup pool: "UI: dark mode, TR/EN, PWA, favourite resources". Most people book the
  same two or three resources again and again; finding them in a list of thirty each time is the
  slow part of booking.
- With the account rather than in the browser, so the phone at the door and the laptop at the
  desk have the same ones.
- The dashboard works out each one's state from what it already has - today's bookings - and
  today's blocked windows, asked once a day, with the same `roomNowFor` as the check-in page and
  the floor plan, rather than a new endpoint saying the same.
- **Trade-offs:**
  - The count isn't locked: two stars at the same moment can make one more than the most.
  - A star shows before the API has it; reloading the page at once can lose it.
  - A blocked window made after the dashboard opened shows from the next day, or a reload.

## ADR-0052: A history of changes, written by the saves themselves

**Decision:**
- Every save writes, in its own transaction, an `AuditEntry` for each audited row it creates,
  changes or deletes: when; who - `Actor` is a signed-in `User`, with their id; `Anonymous`, a
  request without an account (the door page, ADR-0050, or signing up); or `System`, a background
  job; the kind of row and its key; the resource it's about; a few words naming the row as it was
  (`Label`: a resource's name, a booking's two UTC instants); and what changed (`Changes`,
  jsonb: each column before and after, as text). `AuditInterceptor`, an EF Core
  `SaveChangesInterceptor` like `FreedSlotsInterceptor` (ADR-0043), reads it from the change
  tracker, so every way of changing a row is in it, now and later.
- Audited (`AuditedTypes`): bookings and invitations; resources, with their features, teams and
  photo; blocked windows; kinds of resource and office hours, the booking rules; floor plans;
  problems; accounts and their roles. Left out: bookkeeping a job writes (a reminder sent),
  personal lists (favourites, places in a waiting list, the inbox, calendar links), a series' own
  row (each of its bookings is in it), sign-in sessions, and pictures' bytes. A secret - the QR
  code's, a door screen's token, a password - is kept only as whether there is one ("***"). Writes that go straight to the database (`ExecuteUpdate`: the inbox marked read, a
  language, one's own name, sessions) aren't in it.
- `GET /api/audit` (Admin) lists it newest first, 50 a page, the next asked for with `before`, an
  entry's id in the order written; by area (bookings, resources, rules, problems, accounts),
  resource, row, person and time. The people and resources it mentions come named alongside.
  Nothing in it is ever changed or deleted.
- **History** under Admin says it in words: what, which, who, when, and each changed column
  before and after, with filters. A resource's edit page links to its own.

**Why:**
- The plan's backup pool: "audit log: change history with an EF Core SaveChangesInterceptor and an
  admin history screen". "Who cancelled this?" and "who switched check-in off?" had no answer.
- In the save itself, not in each handler: nothing to forget, a change rolled back leaves nothing
  behind, and the next handler is covered without a line.
- Columns as text, so one shape fits every kind, and the screen says it in the reader's language
  and time zone.
- Who as three kinds, so a release for want of a check-in reads as SmartDesk's doing, not as
  nobody's.
- **Trade-offs:**
  - It grows without limit: nothing is deleted from it.
  - A booking made for the first in a waiting list (ADR-0043) is written as done by whoever freed
    the time: it happens in their request.
  - People and resources are named as they're called now.

## ADR-0053: Structured logs with Serilog, searchable in Seq

**Decision:**
- Serilog writes the API's log (`LoggingSetup`): what every `ILogger` says, with each value in a
  message - `{ReservationId}`, `{Count}` - kept as a field of its own. Always to the console, a
  line of text each; and, with `Seq:ServerUrl` set (`Seq:ApiKey` if it asks for one), to Seq,
  where any field can be searched: `StatusCode >= 500`, `UserId = '…'`. Development sends to
  `http://localhost:5341`, where `docker compose up -d` now starts Seq, and the compose file's
  API sends to it as well. How much is written comes from the `Serilog` section of the
  settings, which replaces `Logging`, with the same levels: Information, and ASP.NET Core's own
  lines and EF Core's SQL at Warning.
- A line for every request (`UseSerilogRequestLogging`): its method and path, its status, how
  long it took, and `UserId` when someone is signed in; an exception or a 5xx as an error.
  Health checks are below what's written. Every line written while a request runs carries its
  `UserId` too (`UseUserInLogs`, after authentication), and ASP.NET Core's `RequestId` and the
  trace id, so a request's lines are found together.
- Never in a line: a query string, which carries SignalR's token and a calendar feed's; bodies;
  headers. The path is all that's written of an address, and ASP.NET Core's own request lines,
  which have the whole URL, stay at Warning.
- `GET /health/live` answers `Healthy` while the process runs, for a liveness probe; `/health`
  still says whether it's ready: the database answers, with every migration applied.

**Why:**
- The plan's backup pool: "Logging: structured logging with Serilog + Seq, /health endpoints".
- The messages were written as templates from the start. Serilog keeps their fields rather than
  flattening them into text, so "everything this person did today" or "every 500 this week" is a
  query, not a search through text.
- The host's own logger rather than Serilog's static one, as the tests run several hosts side by
  side; the request lines are handed it, or they'd go to the static one and nowhere.
- **Trade-offs:**
  - Seq is free for one person's development; a team, or production, needs its licence or
    another sink. `ACCEPT_EULA` in docker-compose accepts it for development.
  - The local Seq has no sign-in, like Mailpit: it's for this machine. Its lines hold user ids
    and email addresses (an email sent, a demo user seeded), and in Development without SMTP an
    email itself, a password reset link included (ADR-0019).
  - With Seq down, its lines wait in memory and are dropped past a limit; the console has them all.

## ADR-0054: An app to install, which opens without a network

**Decision:**
- The app is a Progressive Web App: `manifest.webmanifest` (its name, colours and icon, the icon
  as SVG, with a maskable one that fills the square) and Angular's service worker
  (`@angular/service-worker`, `ngsw-config.json`). A phone adds it to the home screen, where it
  opens on its own, without the browser's bars; on a computer, Chrome and Edge offer to install
  it.
- The worker keeps the app itself - `index.html`, its scripts and styles and the Turkish texts,
  fetched in the background after the first visit, and pictures and fonts as they're used - and
  never an answer from the API: `/api`, `/hubs` and `/health` always go to the server, so a
  screen never shows old data from a cache, and nobody's bookings stay on a shared phone.
- Without a network the app still opens, and says it's offline and that nothing can be booked or
  changed until it's back (`ConnectivityService`).
- A new release is fetched in the background, then the app says "A new version of SmartDesk is
  ready" with **Reload** (`AppUpdateService`); a page left open asks for one every hour. A door
  screen, which nobody stands at, reloads by itself, and so does a page whose version can't be
  loaded any more.
- Only in a production build (`provideServiceWorker`, `enabled: !isDevMode()`), so `ng serve`
  and the tests always run what's on disk. nginx checks `ngsw-worker.js` and the manifest again
  on every visit, like `index.html`, rather than keeping them a year like the hashed scripts.

**Why:**
- The plan's backup pool: "UI: dark mode, TR/EN, PWA, favourite resources", the last of it.
- The QR codes at the doors are scanned with phones (ADR-0030, ADR-0050): from the home screen
  the app is a tap away, and it opens where the signal is weak.
- The API's answers stay out of the cache on purpose: bookings change by the minute, live
  updates come over SignalR anyway, and a phone may be shared.
- One SVG draws the icon at every size; Chrome installs with it, which the browser tests check
  with Chrome's own list of reasons not to.
- **Trade-offs:**
  - Offline, the app opens but has nothing new to show: reading bookings offline would mean
    keeping them on the device.
  - An iPhone takes no SVG for its home screen and shows a picture of the page instead, until a
    PNG `apple-touch-icon` is added.
  - The installed app belongs to its address: through a quick tunnel, whose address changes with
    each start (frontend README, "On a phone"), it's installed again each time.
  - `ng serve` has no worker, and changes the pages it serves, so it can't be tried there: the
    whole app in Docker serves the build as it is.
