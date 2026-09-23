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
- Node.js 20+ and Angular CLI 22 (`npm i -g @angular/cli`)
- Docker Desktop (for local PostgreSQL)

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

The API starts on the URL/port in `src/SmartDesk.API/Properties/launchSettings.json`. In
development, OpenAPI docs are served at `/openapi/v1.json`.

## Frontend setup

```bash
cd frontend
npm install
ng serve
```

## Running tests

```bash
dotnet test
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
