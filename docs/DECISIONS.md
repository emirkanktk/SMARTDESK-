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
