# SmartDesk

Office resource & reservation management system - employees book shared resources
(meeting rooms, desks, company cars, equipment) for a time range; the backend rejects
overlapping bookings, and reservation changes reflect live on every open screen via
SignalR.

Docs: [Architecture](docs/ARCHITECTURE.md) · [Data model](docs/DATA_MODEL.md) ·
[API contracts](docs/API_CONTRACTS.md) · [Decisions](docs/DECISIONS.md) ·
[Team sync](docs/TEAM_SYNC.md)

## Prerequisites

- .NET 10 SDK
- `dotnet tool install --global dotnet-ef` (EF Core CLI - not bundled with the SDK)
- Node.js 20+ and Angular CLI 22 (`npm i -g @angular/cli`)
- Docker Desktop (for local PostgreSQL)
- Trust the local HTTPS dev certificate once per machine: `dotnet dev-certs https --trust`
  (skip this and the Angular dev server's calls to the API will fail with a cert error, not
  a CORS one - the symptoms look similar in the browser console, don't confuse them)

## Backend setup

```bash
# 1. Start PostgreSQL
docker compose up -d

# 2. Set the JWT signing key (local dev only - never commit this; at least 32 characters)
dotnet user-secrets set "Jwt:Key" "<a long random string>" --project src/SmartDesk.API

# 3. Apply migrations
dotnet ef database update --project src/SmartDesk.Infrastructure --startup-project src/SmartDesk.API

# 4. Run the API
dotnet run --project src/SmartDesk.API
```

The API starts on the URL/port in `src/SmartDesk.API/Properties/launchSettings.json`
(`https://localhost:7065` by default - matches `frontend/src/environments/environment.development.ts`,
update both together if you change it). In development, Swagger UI is served at `/swagger`
(raw OpenAPI document at `/openapi/v1.json`): call `POST /api/auth/login`, copy the
`accessToken`, and paste it into **Authorize** to try the protected endpoints. Allowed frontend origins for CORS are `Cors:AllowedOrigins` in
`appsettings.Development.json` (see `docs/DECISIONS.md`, ADR-0006).

### Demo users (Development only)

Seeded automatically on startup in the Development environment (see `docs/DECISIONS.md`,
ADR-0007). All three share the password `SmartDesk123!`.

| Email | Role |
|---|---|
| `admin@smartdesk.local` | Admin |
| `employee1@smartdesk.local` | Employee |
| `employee2@smartdesk.local` | Employee |

Five wrong passwords in a row lock an account for five minutes, and one client can try at
most 60 logins a minute in Development (10 elsewhere; `RateLimiting:LoginPermitsPerMinute`).

`GET /health` answers `Healthy` once the API can reach PostgreSQL - a quick check that the
backend is up and configured.

The Development seed also creates eight sample resources (meeting rooms, desks, a company
car, a projector, and a camera under maintenance) when the database has none. The five
resource types come from a migration and exist in every environment (ADR-0010).

## Frontend setup

```bash
cd frontend
npm install
ng serve
```

Serves on `http://localhost:4200` by default - already in the API's CORS allow-list. If
your build serves on a different port, add it to `Cors:AllowedOrigins` too.

## Running tests

```bash
dotnet test               # backend
cd frontend && ng test    # frontend
```

## Project structure

```
src/
  SmartDesk.Domain/          entities, enums, constants - no framework dependencies
  SmartDesk.Application/     commands, queries, handlers, validators (CQRS + MediatR)
  SmartDesk.Infrastructure/  EF Core, PostgreSQL, ASP.NET Identity
  SmartDesk.API/             composition root, controllers, SignalR hubs
tests/
  SmartDesk.Tests/
frontend/
  src/app/features/          one folder per screen (login, dashboard, resources, ...)
  src/app/core/               interceptors, guards, shared services
docs/
  ARCHITECTURE.md, DATA_MODEL.md, API_CONTRACTS.md, DECISIONS.md, TEAM_SYNC.md
docker-compose.yml            local PostgreSQL
```

See `docs/TEAM_SYNC.md` for who owns which part of this structure.
