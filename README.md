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

# 2. Set the JWT signing key (local dev only - never commit this)
dotnet user-secrets set "Jwt:Key" "<a long random string>" --project src/SmartDesk.API

# 3. Apply migrations
dotnet ef database update --project src/SmartDesk.Infrastructure --startup-project src/SmartDesk.API

# 4. Run the API
dotnet run --project src/SmartDesk.API
```

The API starts on the URL/port in `src/SmartDesk.API/Properties/launchSettings.json`
(`https://localhost:7065` by default - matches `frontend/src/environments/environment.development.ts`,
update both together if you change it). In development, OpenAPI docs are served at
`/openapi/v1.json`. Allowed frontend origins for CORS are `Cors:AllowedOrigins` in
`appsettings.Development.json` (see `docs/DECISIONS.md`, ADR-0006).

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

The same checks run on GitHub Actions for every pull request and every push to `main`
(`.github/workflows/ci.yml`): backend build + tests + a check that model changes come with a
migration, and frontend build + tests. A PR's status checks show the result, so a reviewer
can see "still builds and passes tests" before merging.

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
