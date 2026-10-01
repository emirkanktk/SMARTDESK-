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
  they're cancelled or moved ([ADR-0024](docs/DECISIONS.md)).
- **Calendar and dashboard** - reservations in a date range (who booked what), and a daily
  overview: today's reservations, what's in use right now, and your upcoming bookings, with
  "today" computed in the user's time zone.
- **Reminders** - the owner can ask to be reminded 5, 15, 30 or 60 minutes before a reservation
  starts. The reminder arrives in the app, and as a desktop notification when allowed
  ([ADR-0018](docs/DECISIONS.md)).
- **Usage report** - for Admins: how much each resource, type and person booked over up to
  92 days, and how full the office day was (Monday to Friday, 09:00-18:00, in the admin's
  time zone - [ADR-0017](docs/DECISIONS.md)).
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
| Assistant (optional) | Claude, through Anthropic's official .NET SDK |

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
- Docker Desktop (for local PostgreSQL)
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

### Optional: the booking assistant

The assistant is off until the API has an Anthropic API key (ADR-0029). Without one the app
shows no assistant, and everything else works as usual. To turn it on locally:

```bash
dotnet user-secrets set "Assistant:ApiKey" "<your Anthropic API key>" --project src/SmartDesk.API
```

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
| Dashboard | `GET /api/dashboard?timeZone=Europe/Istanbul` |
| Reports | `GET /api/reports/usage?from=&to=&timeZone=` (Admin) |
| Assistant | `GET /api/assistant`, `POST /api/assistant/messages` (optional, ADR-0029) |
| Real-time | SignalR hub `/hubs/reservations`: `ReservationCreated`, `ReservationUpdated`, `ReservationCancelled` |
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
(`.github/workflows/ci.yml`): backend build + tests + a check that model changes come with a
migration, and frontend build + tests. A PR's status checks show the result, so a reviewer
can see "still builds and passes tests" before merging.

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
docker-compose.yml            local PostgreSQL; optional API via the `api` profile
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
