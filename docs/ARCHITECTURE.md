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
other project - it is plain C#. `Application` depends on `Domain` plus MediatR,
FluentValidation and EF Core's provider-agnostic abstractions (`DbSet`, LINQ - see ADR-0009).
`Infrastructure` implements what `Application` needs (persistence, identity) and is the only
place Npgsql/PostgreSQL-specific code lives. `API` is
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
`UnauthorizedException` → 401, `ForbiddenAccessException` → 403, `NotFoundException` →
404, `ConflictException` → 409, anything unexpected → 500 (logged).

### 3.3 Backend project structure

```
src/
  SmartDesk.Domain/
    Entities/          Resource, ResourceType, ResourceFeature, Reservation
    Enums/              ResourceStatus, ReservationStatus
    Constants/          Roles (Admin, Employee)
    Rules/              ReservationOverlap - the one definition of "time range taken"
  SmartDesk.Application/
    Common/             Behaviors/ (ValidationBehavior), Exceptions/, Extensions/,
                          Interfaces/ (incl. IApplicationDbContext), Models/
    Features/<Area>/    Commands/<Name>/ and Queries/<Name>/ - request, handler,
                          validator and response DTO side by side
    DependencyInjection.cs  AddApplication(): MediatR + pipeline + validators
  SmartDesk.Infrastructure/
    Identity/           ApplicationUser, IdentityService, JwtTokenGenerator,
                          JwtOptions, IdentityDataSeeder
    Persistence/         SmartDeskDbContext, Migrations/, Seed/ (resource types,
                          Development demo resources)
    DependencyInjection.cs  AddInfrastructure(): DbContext, Identity, JWT options
  SmartDesk.API/
    Program.cs           composition root: JWT bearer, authorization, CORS,
                          OpenAPI/Swagger UI, exception handling, SignalR, startup seed
    Controllers/          thin - build a command/query, send it through MediatR
    Services/             CurrentUserService (reads the caller from the JWT),
                          SignalRReservationNotifier (NullReservationNotifier remains
                          only as an unused fallback)
    OpenApi/              Bearer security scheme for Swagger UI
    Middleware/           GlobalExceptionHandler
tests/
  SmartDesk.Tests/        unit tests for Application handlers and Domain rules;
                          handlers that query run against SmartDeskDbContext on
                          in-memory SQLite (TestSupport/SqliteTestDatabase)
```

## 4. Data model

See [DATA_MODEL.md](DATA_MODEL.md) for entities, fields, relationships and the business
rules that constrain them (overlap checks, past-date rejection, inactive-resource booking).

## 5. Auth

ASP.NET Core Identity owns user storage and password hashing; roles are plain Identity
roles (`Admin`, `Employee` - see `SmartDesk.Domain.Constants.Roles`), not a raw string
column, so role checks use the framework's own `[Authorize(Roles = ...)]` and
`RoleManager`. Login (`POST /api/auth/login` → `LoginCommand`) checks the password through
`SignInManager` (so Identity's lockout applies) and issues a signed HS256 JWT. A failed
sign-in does the same password-hashing work whether or not the email exists or the account
is locked, so response times don't reveal which emails have accounts. The API validates the
token per request with the JWT bearer handler, using the rules in
`SmartDesk.API/Authentication/JwtBearerSetup.cs`: issuer, audience and key, HS256 only,
and at most 30 seconds of clock skew on the expiry. Every
endpoint requires an authenticated user unless it opts out with `[AllowAnonymous]`
(ADR-0008). The signing key is never committed - see
`docs/DECISIONS.md` (ADR-0005) and the README's setup steps for `dotnet user-secrets`.

## 6. Real-time updates (SignalR)

A single hub (`SmartDesk.API/Hubs/ReservationHub.cs`) broadcasts reservation
create, update and cancel events to every connected client, so an open calendar or
dashboard can patch itself without a reload. The hub has no methods and no business logic.

Handlers don't know about SignalR. After a committed create, update or cancel they call
`IReservationNotifier`. `SignalRReservationNotifier` sends the `ReservationDto` to
`Clients.All` (ADR-0014). A failed broadcast is logged and swallowed, so a down socket
cannot turn a saved reservation into an HTTP error. `NullReservationNotifier` is still
registered with `TryAdd` and only applies if the SignalR notifier is not registered.

The Angular app opens the connection when a session exists and closes it on logout.
Negotiate does not send cookies (`withCredentials: false`); the hub uses the bearer token.
A failed start and a dropped connection both retry forever, at 0s, 2s, 5s, 10s, then
every 30s. Logout cancels a pending retry. Screens subscribe to `created$`, `updated$`,
`cancelled$` and `reconnected$` on `ReservationHubService` rather than the raw connection.

## 7. Frontend architecture

Angular 22, standalone components throughout (no NgModules), feature-based folders under
`frontend/src/app/features/`, one folder per screen from the MVP list (login, dashboard,
resources, availability, reservation, my-reservations, admin). Shared, cross-feature code
(HTTP interceptor, auth guard, typed API clients) lives under `frontend/src/app/core/`.
Forms use Reactive Forms; local/UI state uses Signals rather than a global store, since the
app's state needs (a list of resources, a set of reservations, the current user) are small
enough not to need one. Routing uses a functional route guard that reads the JWT/claims to
gate `Admin`-only routes (e.g. resource management).

### 7.1 Frontend core

```
frontend/src/app/
  core/
    constants/       Roles (mirrors SmartDesk.Domain.Constants.Roles)
    guards/          authGuard, guestGuard, roleGuard(...roles) / adminGuard
    interceptors/    jwtInterceptor, errorInterceptor, ANONYMOUS_REQUEST,
                     SKIP_ERROR_NOTIFICATION
    models/          request/response types, ApiError
    services/        AuthService, NotificationService, ReservationHubService
    utils/           JWT claim decoding, isApiUrl
  layout/
    shell/           top bar + navigation around every signed-in screen
    notification-host/  toast messages
  shared/
    components/      small reusable UI pieces (e.g. StatusBadge)
    utils/           form helpers (applyServerErrors: API 400 messages -> form controls,
                     keepDurationWhenStartMoves), datetime-local <-> Date conversion
    validators/      dateRangeValidator (end after start, max length, start not in the
                     past with an optional grace, end in the future), integerValidator
  features/<screen>/ one lazy-loaded standalone component per screen; sub-screens in
                     sub-folders (e.g. resources/resource-form)
```

Typed API clients (`AuthService`, `ResourceService`, `ReservationService`, ...) live in
`core/services` and return the DTO shapes from `docs/API_CONTRACTS.md` as-is
(`core/models`). Screens hold their own state in signals and never call `HttpClient`
directly.

Times are picked in the browser's local time (`<input type="datetime-local">`) and sent to
the API as UTC ISO strings (`Date.toISOString()`); API timestamps are shown back in local
time. Client-side range rules mirror the API's (e.g. availability's 31-day window is 31 x 24 h
of elapsed time, not calendar days) and only give fast feedback - the API decides. The
reservation form also follows the update rules: once a reservation has started only its end
and note can change, and an unchanged time is sent back as the API returned it, since the
picker drops seconds and any difference would count as a change.

- **Session.** `AuthService` keeps the access token in memory, mirrored to `localStorage`
  so a reload stays signed in. The current user (id, name, email, roles) is read from the
  token's claims, so guards and the top bar work without an extra request. An expired token
  counts as signed out, both when the app loads and on every guarded navigation. The claims
  only drive the UI: the API re-checks the token and roles on every request.
- **Routing.** `/login` is guarded by `guestGuard`. Every other screen is a child of the
  `Shell` route, which is guarded by `authGuard` (`canActivate` + `canActivateChild`). An
  unauthenticated visit is redirected to `/login?returnUrl=...` and sent back after sign-in,
  and only in-app paths are followed. `adminGuard` (`roleGuard(Roles.Admin)`) protects
  `/admin`.
- **HTTP.** `jwtInterceptor` attaches `Authorization: Bearer` to requests for our own API
  only. `errorInterceptor` then turns every failure into an `ApiError` (status, title,
  detail, field errors from `ProblemDetails`).
  - A 401 from our API ends the session and redirects to login, whether or not a token was
    sent: a token that expired while a screen was open is dropped before the request goes
    out, and that request's 401 still has to end the session. The only exception is a
    request marked `ANONYMOUS_REQUEST` (login), whose 401 just means a wrong password.
  - Several requests failing together produce one redirect and one toast.
  - 0, 403, 404 and 5xx show a toast. 400 and 409 are left to the calling screen, because
    they carry form- or rule-specific messages. A caller that shows its own message sets
    `SKIP_ERROR_NOTIFICATION` on the request.
- **Styling.** Plain SCSS. Design tokens (colors, spacing, radius) and shared building blocks
  (`.btn`, `.field`, `.input`, `.card`, `.alert-error`) live in `src/styles.scss`. Component
  styles use those variables instead of hardcoded values.

## 8. Team ownership boundaries

See [TEAM_SYNC.md](TEAM_SYNC.md) for who owns which module and how branches/PRs are
expected to flow between the three of us.
