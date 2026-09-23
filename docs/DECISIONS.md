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
