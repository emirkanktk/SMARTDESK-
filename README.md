# SmartDesk

Office resource & reservation management system - employees book shared resources
(meeting rooms, desks, company cars, equipment) for a time range; the backend rejects
overlapping bookings, and reservation changes reflect live on every open screen via
SignalR.

Docs: [Architecture](docs/ARCHITECTURE.md) · [Data model](docs/DATA_MODEL.md) ·
[API contracts](docs/API_CONTRACTS.md) · [Decisions](docs/DECISIONS.md) ·
[Team sync](docs/TEAM_SYNC.md) · [Demo runbook](docs/DEMO.md) · [UI demo](docs/DEMO_UI.md)

## What it does

- **Front page, sign-in and sign-up** - visitors land on a public page that explains
  SmartDesk and leads to sign-in (email and password, JWT) or sign-up. Signed-in users go
  straight to their dashboard. Three roles: `Employee`, `Manager` (runs a team) and `Admin`.
- **Accounts and teams** - a Manager signs up and an Admin approves them. An Employee signs up
  with their manager's email, and that manager approves them into the team. Managers and
  Admins can also add people with a temporary password, which is changed at the first sign-in.
  Admins change roles, move people between teams and disable accounts
  ([ADR-0020](docs/DECISIONS.md), [ADR-0021](docs/DECISIONS.md)).
- **Resources** - meeting rooms, desks, company cars, projectors, cameras - with type,
  capacity, location, features, a status (`Active` / `Inactive` / `Maintenance`) and a photo,
  or their type's picture until someone uploads one ([ADR-0027](docs/DECISIONS.md)).
  A resource belongs to one or more teams, whose members can see and book it, or it is shared
  by everyone ([ADR-0022](docs/DECISIONS.md)). Admins manage every resource; a Manager adds
  and edits the ones that belong to their team alone. Everyone can browse and filter what
  they can use.
- **Availability search** - which resources are free for a time range, by type and capacity.
- **Blocked windows** - Admins can take a resource out of booking for a specific window
  (maintenance, an event) with a reason that booking attempts then show.
- **Reservations** - book, change, and cancel, up to 180 days ahead. The backend refuses
  overlapping bookings, bookings in the past, and bookings of resources that aren't active,
  and it holds under concurrent requests. The owner or an Admin can change or cancel. A
  resource with bookings still to come can't be put into maintenance or switched off until
  they're cancelled or moved ([ADR-0024](docs/DECISIONS.md)). A time that's taken comes back
  with the nearest free times on the same resource and the others of its kind free then, each
  a click away ([ADR-0042](docs/DECISIONS.md)).
- **Booking rules** - an Admin sets, for each kind of resource, how long a booking may last,
  how many days ahead it may start, how many one person may have at once, and whether it keeps
  to office hours, and sets when the office is open. Every way of booking keeps to them, the
  booking form says them before it asks, and the search and suggestions only offer what they
  allow ([ADR-0046](docs/DECISIONS.md)).
- **Approval** - a kind of resource, a company car say, can wait for an Admin's approval: a
  booking of it is asked for and holds its time, Admins approve or decline it, saying why, on
  **Approvals**, and whoever asked hears in their inbox. A request nobody answers lapses at its
  start ([ADR-0049](docs/DECISIONS.md)).
- **Waiting list** - or wait for that time instead: when it frees up - cancelled, released for
  want of a check-in, ended early or moved - it's booked for whoever joined the line first,
  and their inbox says so. My reservations shows what you wait for and your place in line
  ([ADR-0043](docs/DECISIONS.md)).
- **Calendar and dashboard** - reservations in a date range (who booked what), and a daily
  overview: today's reservations, what's in use right now, and your upcoming bookings, with
  "today" computed in the user's time zone.
- **In your own calendar** - "Add to calendar" on a booking or an invitation gives an `.ics`
  file that Outlook, Google Calendar or a phone's calendar adds. Or make a personal calendar link
  on your profile and subscribe to it once: the calendar then shows your bookings and the
  meetings you're invited to, and keeps up with them by itself. A new link cuts off the old one
  ([ADR-0048](docs/DECISIONS.md)).
- **Floor plan** - each floor's plan, a picture an Admin uploads, with the floor's resources on
  it: green when free, red in use, grey out of use, now or at a chosen time, and changing live.
  Choosing one opens the booking form from when it's free. Admins place resources by dragging
  them or with the arrow keys; a manager places their team's own
  ([ADR-0041](docs/DECISIONS.md)).
- **Reminders** - the owner can ask to be reminded 5, 15, 30 or 60 minutes before a reservation
  starts. The reminder arrives in the app, and as a desktop notification when allowed
  ([ADR-0018](docs/DECISIONS.md)).
- **Check-in with a QR code** - a room can ask for its bookings to be checked in: whoever
  booked it scans the QR code at the door from 10 minutes before the start until 15 minutes
  after (a setting). A booking nobody checks in to is released, so the room goes back to
  people who are there. The resource form shows the code and a sheet to print; switched off,
  nothing changes ([ADR-0030](docs/DECISIONS.md)). To try it on one machine, switch it on for
  a room, book the room for a few minutes from now, and open the link under its QR code.
- **A door screen** - a tablet at the door shows the room's day and a QR code that changes every
  30 seconds, so only someone standing there can check in. At the door you can also book a free
  room on the spot, make a booking run longer or end it, and five minutes before a release
  everyone on the booking is warned ([ADR-0034](docs/DECISIONS.md)).
- **Reporting a problem at the door** - the QR code at a resource's door also opens a way to
  report a problem with it: what it's about, a few words, a photo from the phone's camera, and
  whether it can't be used meanwhile. The page lists the problems already reported and not fixed
  yet, and the same problem isn't reported twice. Every resource has a QR code now, check-in or
  not, and **QR labels** prints many at once, eight to an A4 sheet with lines to cut along
  ([ADR-0037](docs/DECISIONS.md)).
- **Reported problems** - Admins, and managers for their team's own resources, see reports come
  in live, with a toast and a count in the menu. They take them up and resolve them, and the
  reporter is told in their inbox. A broken resource can be taken out of use until a chosen time:
  the screen first says how many bookings that cancels, and their people are told why
  ([ADR-0038](docs/DECISIONS.md)).
- **Bookings that repeat** - every day, every weekday, every week or every 2 weeks until a last
  day: the form lists the dates first, those that aren't free with why, and books them all at
  once or only the free ones. Each date is a booking of its own; the rest of a series can be
  cancelled from any date, and those invited answer once for every date
  ([ADR-0035](docs/DECISIONS.md)).
- **Running over** - ten minutes before a booking ends, its owner is asked whether they need
  longer, and extends it by 15, 30 or 60 minutes with a click, only as far as the room is free;
  everyone invited is told ([ADR-0033](docs/DECISIONS.md)). A booking in progress also has
  **Extend** in My reservations.
- **Meetings and an inbox** - a manager books a room and ticks whom of the team to meet; an
  admin may invite anyone. Each person gets a message in their SmartDesk inbox, and another if
  the meeting moves or is cancelled, and answers there: coming, or not and why. The organizer
  sees every answer, and the reason only they read ([ADR-0032](docs/DECISIONS.md)).
- **Usage report** - for Admins: how much each resource, type and person booked over up to
  92 days, and how full the office day was (the office's hours, Monday to Friday 09:00-18:00
  until an Admin changes them, in the admin's time zone - [ADR-0017](docs/DECISIONS.md)). It shows when the office is busiest, as a heat
  map of weekdays and hours, and how often bookings go unused: the share cancelled, and the
  share of check-ins nobody came to ([ADR-0044](docs/DECISIONS.md)). It downloads as an Excel
  workbook, in the app's language ([ADR-0045](docs/DECISIONS.md)).
- **Live updates** - after a reservation is created, changed, or cancelled, every connected
  screen receives `ReservationCreated`, `ReservationUpdated`, or `ReservationCancelled`
  ([ADR-0014](docs/DECISIONS.md)).
- **Turkish and English** - the whole app, in Turkish unless the user picks English. The
  API's messages follow the language on screen, and emails go out in the recipient's
  language ([ADR-0023](docs/DECISIONS.md), [ADR-0025](docs/DECISIONS.md)).
- **Booking assistant (optional)** - with an Anthropic API key, a conversation in the corner of
  every screen: "a room for 4 tomorrow at 10". It looks things up as the user and offers
  bookings and cancellations as cards. Nothing is booked until the user presses a card's button
  ([ADR-0029](docs/DECISIONS.md)).

| Layer | Stack |
|---|---|
| Frontend | Angular 22 (standalone components, Signals, Reactive Forms) |
| Backend | .NET 10 Web API, Onion Architecture, CQRS with MediatR, FluentValidation |
| Data | PostgreSQL via EF Core; ASP.NET Core Identity for users and roles |
| Real-time | SignalR |
| Assistant (optional) | Claude, through Anthropic's official .NET SDK, or Gemini over its REST API (ADR-0036) |

### Design highlights

- **Overlaps are impossible, not just unlikely.** Bookings of one resource are serialized by
  a row lock, and a PostgreSQL exclusion constraint rejects an overlapping row however it
  arrives. Tested with 30 simultaneous requests for the same slot: exactly one succeeds, the
  rest get a clean 409 ([ADR-0011](docs/DECISIONS.md), [ADR-0013](docs/DECISIONS.md)).
- **One overlap rule everywhere.** Search, booking, updates and the calendar all use the
  same half-open `[start, end)` definition from the Domain layer, so back-to-back bookings
  work and "shown as free" and "can be booked" can't disagree.
- **Times are UTC on the wire, "today" is local.** The dashboard takes the browser's IANA
  time zone, so the day boundary is right in Istanbul and on daylight-saving days
  ([ADR-0012](docs/DECISIONS.md)).
- **Secure by default.** Every endpoint requires a signed-in user unless explicitly marked
  anonymous; account lockout plus a per-client login rate limit ([ADR-0008](docs/DECISIONS.md)).
- **Every error is RFC 7807 ProblemDetails**, and the 409 messages are written to be shown to
  users as-is.

## Prerequisites

- .NET 10 SDK
- `dotnet tool install --global dotnet-ef` (EF Core CLI - not bundled with the SDK)
- Node.js 22.22+ or 24.15+ (what Angular 22 supports; CI uses 24) and Angular CLI 22
  (`npm i -g @angular/cli`)
- Docker Desktop (for local PostgreSQL, or to run the whole app in containers)
- Trust the local HTTPS dev certificate once per machine: `dotnet dev-certs https --trust`
  (skip this and the Angular dev server's calls to the API will fail with a cert error, not
  a CORS one - the symptoms look similar in the browser console, don't confuse them)

## Backend setup

```bash
# 1. Start PostgreSQL and Mailpit (catches the emails the API sends)
docker compose up -d

# 2. Set the JWT signing key (local dev only - never commit this; at least 32 characters)
dotnet user-secrets set "Jwt:Key" "<a long random string>" --project src/SmartDesk.API

# 3. Apply migrations
dotnet ef database update --project src/SmartDesk.Infrastructure --startup-project src/SmartDesk.API

# 4. Run the API
dotnet run --project src/SmartDesk.API --launch-profile https
```

The `https` launch profile in `src/SmartDesk.API/Properties/launchSettings.json` serves
`https://localhost:7065` (and `http://localhost:5291`). `7065` is what
`frontend/src/environments/environment.development.ts` calls, so update both together if you
change it. A bare `dotnet run` picks the first profile, `http`, which listens on
`http://localhost:5291` only: enough for Swagger (as in `docs/DEMO.md`), but the dev frontend
can't reach it. In development, Swagger UI is served at `/swagger`
(raw OpenAPI document at `/openapi/v1.json`): call `POST /api/auth/login`, copy the
`accessToken`, and paste it into **Authorize** to try the protected endpoints. Allowed
frontend origins for CORS are `Cors:AllowedOrigins` in `appsettings.Development.json` (see
`docs/DECISIONS.md`, ADR-0006).

**Email.** The API sends email for password resets (ADR-0019) and for sign-ups, approvals and
new accounts (ADR-0020). In Development it goes to
**Mailpit**, started by `docker compose up -d`: open `http://localhost:8025` to read it. Links
in emails point at `App:FrontendUrl` (`http://localhost:4200` in Development; required
elsewhere). For a real mail server, set `Email:Smtp:Host`, `Port` and `From`, plus
`Username` / `Password` as environment variables or secrets. Outside Development the API
refuses to start without `Email:Smtp:Host`. In Development, leaving it out writes each email,
link included, to the API log instead.

**Check-in.** A booking of a resource that asks for a check-in is released when nobody has
checked in `CheckIn:GraceMinutes` after its start: 15 by default, from 1 to 60 (ADR-0040).
For a demo, `CheckIn__GraceMinutes=1` shows a release within a minute or so; the app reads the
setting from the API, so its texts and times follow.

### Demo users (Development only)

Seeded automatically on startup in the Development environment (see `docs/DECISIONS.md`,
ADR-0007). All four share the password `SmartDesk123!`.

| Email | Role |
|---|---|
| `admin@smartdesk.local` | Admin - manages every account, resource and block |
| `manager@smartdesk.local` | Manager - manages their own team (both employees below) and its resources (Meeting Room B, Projector P-1) |
| `employee1@smartdesk.local` | Employee, in the manager's team |
| `employee2@smartdesk.local` | Employee, in the manager's team |

**Accounts (ADR-0020, ADR-0021).** Anyone can sign up as a **Manager**, whom an Admin
approves, or as an **Employee** by giving their manager's email, and that manager approves
them into the team. Both sides are emailed. A Manager can also add team members directly with
a temporary password, which the member must change at their first sign-in. Admins can add
accounts of any role the same way, change roles, move employees between teams, disable or
re-enable accounts, and set temporary passwords. An account that is disabled, or whose role or
password changes, is signed out everywhere on its next request. The endpoints
are in [API contracts](docs/API_CONTRACTS.md) under Users and Team.

**Outside Development** there are no demo users. Give the first admin through configuration,
as environment variables or a secret, never in appsettings (ADR-0016):

```bash
export BootstrapAdmin__Email="it-admin@company.example"
export BootstrapAdmin__Password="<a strong password>"
```

On startup the API creates that account if it doesn't exist and makes sure it is an Admin.
An existing account keeps its password, so the variables can stay or be removed once the admin
has signed in. Setting only one of the two stops startup with an error. With neither set and
no admin in the database, the API logs a warning.

Five wrong passwords in a row lock an account for five minutes, and one client can try at
most 60 logins a minute in Development (10 elsewhere; `RateLimiting:LoginPermitsPerMinute`).

`GET /health` answers `Healthy` once the API can reach PostgreSQL and every migration has
been applied - a quick check that the backend is up and usable.

**A lived-in office for a demo (ADR-0047).** On a fresh Development database - migrated, with
no bookings yet - run the API once with `--demo-data`:

```bash
dotnet run --project src/SmartDesk.API -- --demo-data
```

It adds two more managers and fifteen people in three teams (Turkish names from Bogus, emails
like `onur.beserler@smartdesk.local`, the same password), rooms and desks on three floors with
their plans, six weeks of bookings behind and two ahead - checked in, released, cancelled,
meetings with people invited, two weekly series - a few problem reports, a blocked window and
a waiting list. Then it stops; start the API as usual. The usage report, the floor plan and the
calendars have something to show from the first minute. It's the same office on every run,
relative to the day, and it leaves Meeting Room A and the Board Room free from now on, for
the walkthrough in [docs/DEMO_UI.md](docs/DEMO_UI.md). On a database with bookings it does
nothing, and outside Development it refuses.

Migrations are applied on purpose (step 3 above). Setting `Database:MigrateOnStartup` to
`true` (environment variable `Database__MigrateOnStartup=true`) makes the API apply pending
migrations itself before seeding - meant for a throwaway database such as a container's,
not for a shared one.

### Optional: run the API in Docker

`docker compose up -d` starts PostgreSQL and Mailpit only. To run the API next to them, set a
signing key of at least 32 characters and start the `api` profile. Do not also `dotnet run`
against the same database if you only wanted one of them; they can share PostgreSQL, but
the container listens on port 8080, not the launchSettings ports.

```bash
export JWT_KEY="<a long random string>"
docker compose --profile api up -d --build
curl http://localhost:8080/health
```

The container uses `ASPNETCORE_ENVIRONMENT=Development` and
`Database__MigrateOnStartup`, so a fresh database is migrated and the demo users and
sample resources are seeded. This is a local stand-in, not a production image. Point the
Angular dev server at `http://localhost:8080/api` if you use this profile instead of
`dotnet run`.

The Development seed also creates eight sample resources (meeting rooms, desks, a company
car, a projector, and a camera under maintenance) when the database has none. The five
resource types come from a migration and exist in every environment (ADR-0010).

### Optional: the whole app in Docker

The `app` profile runs everything in containers: PostgreSQL, Mailpit, the API, and the
app's production build behind nginx (ADR-0031). No .NET SDK or Node.js needed, only Docker:

```bash
export JWT_KEY="<a long random string>"
docker compose --profile app up -d --build
```

Then open `http://localhost:4200` and sign in as one of the demo users above (a production
build doesn't list them on the front page). That's the address `ng serve` uses, so stop
`ng serve` first. The API's Development settings name it too, so links in emails work.

- nginx serves the app and passes `/api` and `/hubs` (SignalR's WebSocket) to the API, so
  the browser sees one origin, as with `ng serve`. The session cookie needs that (ADR-0026).
- The API believes nginx's `X-Forwarded-For` (ADR-0028), so each user has their own sign-in
  limit. For that, nginx has a fixed address on a network of its own, `172.28.42.0/24` in
  `docker-compose.yml`. If that subnet is taken on your machine, change it and the two
  addresses that go with it.
- nginx's access log leaves out query strings, which carry SignalR's token and a password
  reset link's.
- Like the `api` profile, this is a local stand-in in Development with the demo data, not a
  production setup: there's no HTTPS, and the API's port 8080 is open too.

`docker compose --profile app down` stops it; add `-v` to drop the database as well.

### Optional: the booking assistant

The assistant is off until the API has a key for its model (ADR-0029). Without one the app
shows no assistant, and everything else works as usual. To turn it on locally with Claude:

```bash
dotnet user-secrets set "Assistant:ApiKey" "<your Anthropic API key>" --project src/SmartDesk.API
```

Or with Google's Gemini (ADR-0036), with a key from Google AI Studio:

```bash
dotnet user-secrets set "Assistant:Provider" "Gemini" --project src/SmartDesk.API
dotnet user-secrets set "Assistant:ApiKey" "<your Gemini API key>" --project src/SmartDesk.API
```

Restart the API after changing either. The model is `Assistant:Model` for Claude and
`Assistant:GeminiModel` for Gemini (`gemini-flash-latest`, Google's newest Flash).

Elsewhere, set the environment variable `Assistant__ApiKey`; never put the key in
`appsettings`. The other settings are in `appsettings.json` under `Assistant`: `Model`
(`claude-opus-5-5`), `Effort` (`low`), `MaxTokens`, `MaxToolRounds` and `TimeoutSeconds` (`90`,
the most one answer may take before the user is asked to try again). Each question is a
paid call to the model. A user can ask 20 a minute (`RateLimiting:AssistantPermitsPerMinute`),
and the API log shows the tokens each answer used.

### Behind a reverse proxy

In production the API sits behind a reverse proxy that terminates HTTPS (nginx, a load
balancer). The API reads the client's address and the original scheme from the proxy's
`X-Forwarded-For` and `X-Forwarded-Proto` headers, but only when the request comes from a
proxy it trusts (ADR-0028). A proxy on the same machine is trusted as it is. Any other proxy
has to be listed, or every user shares one sign-in rate limit and HSTS is never sent:

```bash
ForwardedHeaders__KnownProxies__0=10.0.0.5         # the proxy's address, or
ForwardedHeaders__KnownNetworks__0=172.18.0.0/16   # the network it's on, e.g. a Docker network
```

### API at a glance

Full shapes, rules and error codes: [`docs/API_CONTRACTS.md`](docs/API_CONTRACTS.md).

| Area | Endpoints |
|---|---|
| Auth | `POST /api/auth/login`, `GET /api/auth/me` |
| Resources | `GET /api/resource-types`, `GET /api/resources`, `GET /api/resources/{id}`, `GET /api/resources/available`, `POST /api/resources` (Admin), `PUT /api/resources/{id}` (Admin), `PATCH /api/resources/{id}/status` (Admin) |
| Resource blocks | `GET /api/resource-blocks?from=&to=`, `POST /api/resource-blocks` (Admin), `DELETE /api/resource-blocks/{id}` (Admin) |
| Reservations | `POST /api/reservations`, `GET /api/reservations?from=&to=` (calendar), `GET /api/reservations/mine`, `GET /api/reservations/{id}`, `PUT /api/reservations/{id}`, `POST /api/reservations/{id}/cancel` |
| Series | `GET /api/reservations/series/preview`, `POST /api/reservations/series`, `POST /api/reservations/{id}/cancel-following` (ADR-0035) |
| Running over | `GET /api/reservations/{id}/extension`, `POST /api/reservations/{id}/extend` (ADR-0033) |
| Calendar | `GET /api/reservations/{id}/calendar` (an `.ics` file), `GET`/`POST`/`DELETE /api/calendar/subscription`, `GET /api/calendar/feed.ics?token=` (the link's token, no user) (ADR-0048) |
| Approvals | `GET /api/approvals`, `POST /api/approvals/{id}/approve`, `POST /api/approvals/{id}/decline` (Admin) (ADR-0049) |
| Booking rules | `GET /api/booking-rules`, `PUT /api/booking-rules/office-hours` (Admin), `PUT /api/booking-rules/types/{id}` (Admin) (ADR-0046) |
| Door screen | `POST`/`DELETE /api/resources/{id}/door-screen`, `GET /api/door/{id}` (the screen's token, no user), `POST /api/reservations/{id}/end` (ADR-0034) |
| Meetings | `GET /api/reservations/invitees`, `GET /api/reservations/invited`, `POST /api/reservations/{id}/response` (ADR-0032) |
| Inbox | `GET /api/inbox`, `GET /api/inbox/unread-count`, `POST /api/inbox/{id}/read`, `POST /api/inbox/read-all` |
| Dashboard | `GET /api/dashboard?timeZone=Europe/Istanbul` |
| Reports | `GET /api/reports/usage?from=&to=&timeZone=` (Admin) |
| Assistant | `GET /api/assistant`, `POST /api/assistant/messages` (optional, ADR-0029) |
| Real-time | SignalR hub `/hubs/reservations`: `ReservationCreated`, `ReservationUpdated`, `ReservationCancelled`; to one user: `ReservationStartingSoon`, `ReservationReleased`, `ReservationReleaseWarning`, `ReservationEndingSoon`, `InboxMessageReceived` |
| Ops | `GET /health` |

## Frontend setup

```bash
cd frontend
npm install
ng serve
```

Serves on `http://localhost:4200` by default, and proxies `/api` and `/hubs` to the API on
`https://localhost:7065` (`frontend/proxy.conf.json`). The app and the API then share one
origin, as they do in production, which is what lets the session cookie work (ADR-0026). The
front page lists the demo accounts in development builds only.
`ng serve --configuration docker` proxies to an API on `http://localhost:8080` instead, for an
API running in Docker (`proxy.docker.json`) - no need to edit anything.

## Running tests

```bash
dotnet test               # unit tests, plus integration tests (Docker must be running)
dotnet test --filter Category!=Integration   # unit tests only, no Docker
cd frontend && ng test    # frontend
cd frontend && npm run test:time-zones   # frontend, once per time zone (DST, UTC+05:45, ...)
```

Run the time-zone variant after touching date or time code: it sets `TZ` for each run
itself, which a `TZ=...` prefix in Git Bash on Windows silently doesn't.

The same checks run on GitHub Actions for every pull request and every push to `main`
(`.github/workflows/ci.yml`):
- backend build + tests + a check that model changes come with a migration;
- frontend: every text has its Turkish version (`npm run i18n:check`), the code is formatted
  (`npm run format:check`), the specs and the browser tests type-check, build, tests, and the
  tests again in other time zones;
- the browser tests (`npm run e2e`, see `frontend/README.md`): PostgreSQL, the API with the
  Development seed and `ng serve`, driven in Chromium. A failed run keeps the Playwright report,
  the traces and both logs as an artifact for a week.
- the whole app in Docker (`docker compose --profile app`, ADR-0031): both images are built,
  and some of the browser tests run through nginx.

A PR's status checks, or the run on `main`, show whether it still builds and passes its tests.

## Project structure

```
src/
  SmartDesk.Domain/          entities, enums, constants, domain rules (reservation overlap)
                             - no framework dependencies
  SmartDesk.Application/     Features/<Area>/Commands|Queries - request, handler, validator,
                             DTO side by side (CQRS + MediatR); Common/ for the pipeline,
                             exceptions and interfaces Infrastructure implements
  SmartDesk.Infrastructure/  EF Core + PostgreSQL (DbContext, migrations, seed data),
                             ASP.NET Identity, JWT, email, the assistant's Claude client
  SmartDesk.API/             composition root, controllers, SignalR hub, error handling,
                             rate limiting, health check
tests/
  SmartDesk.Tests/           unit tests: handlers against EF Core on in-memory SQLite,
                             validators, domain rules
tools/
  SmartDesk.BookingRace/     reproduces ADR-0011's concurrency test against a running API
                             (see docs/DEMO.md)
frontend/
  src/app/features/          one folder per screen (login, dashboard, resources, ...)
  src/app/core/               interceptors, guards, shared services
docs/
  ARCHITECTURE.md, DATA_MODEL.md, API_CONTRACTS.md, DECISIONS.md, TEAM_SYNC.md
docker-compose.yml            local PostgreSQL and Mailpit; the API (`api` profile), or the API
                             and the app behind nginx (`app` profile, frontend/Dockerfile)
```

See `docs/TEAM_SYNC.md` for who owns which part of this structure.

## Photo credits

The pictures shown for each resource type are free photos from
[Unsplash](https://unsplash.com), used under the [Unsplash License](https://unsplash.com/license),
resized and re-encoded for the app (`frontend/public/images/resource-types`):

| Picture | Source |
|---|---|
| Meeting Room | [photo-1431540015161-0bf868a2d407](https://images.unsplash.com/photo-1431540015161-0bf868a2d407) |
| Desk | [photo-1518455027359-f3f8164ba6bd](https://images.unsplash.com/photo-1518455027359-f3f8164ba6bd) |
| Company Car | [photo-1549317661-bd32c8ce0db2](https://images.unsplash.com/photo-1549317661-bd32c8ce0db2) |
| Projector | [photo-1579036095242-fe07594274ca](https://images.unsplash.com/photo-1579036095242-fe07594274ca), by Chirayu Trivedi |
| Camera | [photo-1502920917128-1aa500764cbd](https://images.unsplash.com/photo-1502920917128-1aa500764cbd) |
