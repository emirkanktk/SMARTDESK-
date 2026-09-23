# SmartDesk Architecture

## 1. Overview

SmartDesk is an office resource & reservation management system. Employees browse shared
resources (meeting rooms, desks, company cars, equipment), check availability for a time
range, and book them; the system rejects overlapping bookings for the same resource and
reflects reservation changes to every open screen in real time.

## 2. Tech stack

| Layer | Technology |
|---|---|
| Frontend | Angular 22, standalone components, Signals, Reactive Forms |
| Backend | .NET 10 Web API |
| Architecture | Onion Architecture |
| Application layer | CQRS + MediatR |
| Data access | Entity Framework Core |
| Database | PostgreSQL (Npgsql) |
| Auth | ASP.NET Core Identity + JWT bearer tokens |
| Real-time | SignalR |
| API docs | Built-in ASP.NET Core OpenAPI (`/openapi/v1.json`) + Swagger UI (`/swagger`), Development only |
| Local infra | Docker Compose (PostgreSQL) |

## 3. Backend: Onion Architecture

```
                    ┌─────────────────────────┐
                    │   SmartDesk.API          │  Controllers, auth config,
                    │   (composition root)     │  SignalR hubs, middleware
                    └────────────┬─────────────┘
                                 │ depends on
                    ┌────────────▼─────────────┐
                    │   SmartDesk.Infrastructure│  EF Core, Npgsql, Identity,
                    │                           │  DbContext, migrations
                    └────────────┬─────────────┘
                                 │ depends on
                    ┌────────────▼─────────────┐
                    │   SmartDesk.Application   │  Commands, Queries, Handlers,
                    │                           │  DTOs, validators, interfaces
                    └────────────┬─────────────┘
                                 │ depends on
                    ┌────────────▼─────────────┐
                    │   SmartDesk.Domain        │  Entities, enums, constants -
                    │   (no outward references) │  no framework dependencies
                    └───────────────────────────┘
```

Dependencies point inward only. `Domain` never references EF Core, ASP.NET Core, or any
other project - it is plain C#. `Application` depends only on `Domain` plus MediatR/
FluentValidation abstractions. `Infrastructure` implements what `Application` needs
(persistence, identity) and is the only place EF Core/Npgsql-specific code lives. `API` is
the composition root: it wires everything together in `Program.cs` and exposes it over HTTP.

### 3.1 Why the `ApplicationUser` identity type lives in Infrastructure, not Domain

ASP.NET Core Identity's `IdentityUser<TKey>` is a framework type. Putting a class that
extends it in `Domain` would pull an ASP.NET Core dependency into the one project that is
supposed to have none. `SmartDesk.Infrastructure.Identity.ApplicationUser` extends
`IdentityUser<Guid>`; domain entities that need to reference "the user who booked this"
(`Reservation.UserId`) just store a `Guid`, with no compile-time dependency on Identity.

### 3.2 CQRS + MediatR flow

Every write is a `Command`, every read is a `Query`, both handled by a single `Handler` in
`SmartDesk.Application`, dispatched through MediatR from a thin API controller:

```
Controller  →  IMediator.Send(command/query)  →  Handler  →  DbContext  →  PostgreSQL
```

Handlers are where business rules live (e.g. `CreateReservationCommandHandler` runs the
overlap check against the database - never trust a check the frontend already did, the
frontend's own check is only a fast-feedback convenience). FluentValidation validators for
each Command/Query run in `ValidationBehavior`, a MediatR pipeline behavior, ahead of the
handler; a failure never reaches the handler and comes back as a 400.

Application code that needs something from outside (Identity, token signing, the current
HTTP caller) asks for it through an interface in `Application/Common/Interfaces`
(`IIdentityService`, `IJwtTokenGenerator`, `ICurrentUserService`); Infrastructure or API
provides the implementation. That keeps handlers unit-testable with plain mocks.

Exceptions carry no HTTP knowledge. `GlobalExceptionHandler` is the single place that maps
them to `ProblemDetails` responses: FluentValidation's `ValidationException` → 400,
`UnauthorizedException` → 401, anything unexpected → 500 (logged).

### 3.3 Backend project structure

```
src/
  SmartDesk.Domain/
    Entities/          Resource, ResourceType, ResourceFeature, Reservation
    Enums/              ResourceStatus, ReservationStatus
    Constants/          Roles (Admin, Employee)
  SmartDesk.Application/
    Common/             Behaviors/ (ValidationBehavior), Exceptions/, Interfaces/, Models/
    Features/<Area>/    Commands/<Name>/ and Queries/<Name>/ - request, handler,
                          validator and response DTO side by side
    DependencyInjection.cs  AddApplication(): MediatR + pipeline + validators
  SmartDesk.Infrastructure/
    Identity/           ApplicationUser, IdentityService, JwtTokenGenerator,
                          JwtOptions, IdentityDataSeeder
    Persistence/         SmartDeskDbContext, Migrations/
    DependencyInjection.cs  AddInfrastructure(): DbContext, Identity, JWT options
  SmartDesk.API/
    Program.cs           composition root: JWT bearer, authorization, CORS,
                          OpenAPI/Swagger UI, exception handling, SignalR, startup seed
    Controllers/          thin - build a command/query, send it through MediatR
    Services/             CurrentUserService (reads the caller from the JWT)
    OpenApi/              Bearer security scheme for Swagger UI
    Middleware/           GlobalExceptionHandler
tests/
  SmartDesk.Tests/        unit tests for Application handlers and Domain rules
```

## 4. Data model

See [DATA_MODEL.md](DATA_MODEL.md) for entities, fields, relationships and the business
rules that constrain them (overlap checks, past-date rejection, inactive-resource booking).

## 5. Auth

ASP.NET Core Identity owns user storage and password hashing; roles are plain Identity
roles (`Admin`, `Employee` - see `SmartDesk.Domain.Constants.Roles`), not a raw string
column, so role checks use the framework's own `[Authorize(Roles = ...)]` and
`RoleManager`. Login (`POST /api/auth/login` → `LoginCommand`) checks the password through
`SignInManager` (so Identity's lockout applies) and issues a signed HS256 JWT; the API
validates it per request via the JWT bearer handler configured in `Program.cs`. Every
endpoint requires an authenticated user unless it opts out with `[AllowAnonymous]`
(ADR-0008). The signing key is never committed - see
`docs/DECISIONS.md` (ADR-0005) and the README's setup steps for `dotnet user-secrets`.

## 6. Real-time updates (SignalR)

A single hub (`SmartDesk.API/Hubs/ReservationHub.cs`) broadcasts reservation
create/cancel events to connected clients so an open calendar/dashboard screen updates
without a page reload. The hub itself carries no business logic - handlers publish to it
after a successful write. Wiring (SignalR service registration, hub mapping) is part of
the shared skeleton; the actual broadcast calls from handlers, reconnect/backoff handling,
and the Angular-side connection service are Burak's scope (integration).

## 7. Frontend architecture

Angular 22, standalone components throughout (no NgModules), feature-based folders under
`frontend/src/app/features/`, one folder per screen from the MVP list (login, dashboard,
resources, availability, reservation, my-reservations, admin). Shared, cross-feature code
(HTTP interceptor, auth guard, typed API clients) lives under `frontend/src/app/core/`.
Forms use Reactive Forms; local/UI state uses Signals rather than a global store, since the
app's state needs (a list of resources, a set of reservations, the current user) are small
enough not to need one. Routing uses a functional route guard that reads the JWT/claims to
gate `Admin`-only routes (e.g. resource management).

## 8. Team ownership boundaries

See [TEAM_SYNC.md](TEAM_SYNC.md) for who owns which module and how branches/PRs are
expected to flow between the three of us.
