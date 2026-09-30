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
place Npgsql/PostgreSQL-specific code lives. It also sets how queries run: a query that loads
collections sends one statement per collection (split queries), so a resource's features and
teams don't come back multiplied by each other. `API` is
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
and at most 30 seconds of clock skew on the expiry. The token must also name a sign-in
session that's still open (ADR-0026): the access token lasts 15 minutes, and the app renews it
from a refresh token that lives only in an HttpOnly cookie. Every
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
Negotiate does not send cookies (`withCredentials: false`); the hub uses the bearer token,
renewed first if it's about to expire, on every (re)connect.
A failed start and a dropped connection both retry forever, at 0s, 2s, 5s, 10s, then
every 30s. Logout cancels a pending retry. Screens subscribe to `created$`, `updated$`,
`cancelled$` and `reconnected$` on `ReservationHubService` rather than the raw connection.

## 7. Frontend architecture

Angular 22, standalone components throughout (no NgModules), feature-based folders under
`frontend/src/app/features/`, one folder per screen: the MVP list (login, dashboard,
resources, availability, reservation, my-reservations, admin) plus home (the public front
page), calendar, reports and resource-blocks. Shared, cross-feature code
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
    i18n/            the language (Turkish by default), words for the API's statuses and roles
    theme/           ThemeService: light or dark, the system's setting until the user picks
    guards/          authGuard, guestGuard, sessionGuard, roleGuard(...roles) / adminGuard,
                     managerGuard, resourceEditorGuard
    interceptors/    languageInterceptor, jwtInterceptor, errorInterceptor, ANONYMOUS_REQUEST,
                     SKIP_ERROR_NOTIFICATION
    models/          request/response types, ApiError
    services/        AuthService, NotificationService, ReservationHubService
    utils/           JWT claim decoding, isApiUrl
  layout/
    shell/           navigation and the user around every signed-in screen: a sidebar on
                     wide screens, a bar on top on narrower ones
    profile-menu/    the user's menu: profile, password page, sign out
    notification-host/  toast messages
  shared/
    components/      small reusable UI pieces (e.g. StatusBadge, LanguageSwitch, ThemeSwitch,
                     Logo, Icon, Backdrop, AuthShowcase)
    directives/      HideOnScroll: a bar on top gets out of the way while the page scrolls
                     down, and is back on the way up
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

- **Session (ADR-0026).** `AuthService` keeps the access token in memory only; nothing a
  script could steal is stored. The session itself is an HttpOnly cookie that only
  `/api/auth/refresh` and `/api/auth/logout` see.
  - At start-up, an app initializer calls `restoreSession()`, so a reload stays signed in.
  - A minute before the token expires, the service renews it. A token that expired anyway (the
    laptop slept) is renewed on the next request or guarded navigation.
  - Only one tab renews at a time (the Web Locks API), since each renewal replaces the shared
    cookie. Tabs tell each other about signing in and out over a `BroadcastChannel`.
  - A renewal ends in one of three ways: a new token, the API saying the session is over
    (sign out here), or no answer. With no answer the user stays signed in (`isAuthenticated()`
    stays true while the token may have expired), guards let navigation through, and the
    service tries again after 10, 20 and 40 seconds, then every minute.
  - A sign-out that can't reach the server is noted in `localStorage` and finished before the
    next renewal, so the cookie's session isn't taken up again.
  - The current user (id, name, email, roles) is read from the token's claims, so guards and
    the top bar work without an extra request. The claims only drive the UI: the API
    re-checks the token, its session and the roles on every request.
- **Routing.** The public front page (`/`) and `/login` are guarded by `guestGuard`, which
  sends a signed-in user to `/dashboard`; an unknown address goes to `/`. Every other screen
  is a child of the `Shell` route, which is guarded by `authGuard` (`canActivate` +
  `canActivateChild`). An unauthenticated visit is redirected to `/login?returnUrl=...` and
  sent back after sign-in, and only in-app paths are followed. `adminGuard`
  (`roleGuard(Roles.Admin)`) protects `/admin` and its sub-screens (`/admin/reports`, `/admin/users`,
  `/admin/blocks`). `managerGuard` protects `/team`. `resourceEditorGuard` lets admins and
  managers open the resource forms; the form itself mirrors the API's rule
  (`canManageResource` in `shared/utils/resource-access.ts`) and shows a manager a note instead
  of a form for a resource that isn't their team's alone (ADR-0022). `/register` is
  guest-only too. A session whose password was set by someone else is sent to
  `/change-password` by `authGuard` (and by the error interceptor on the API's
  `password-change-required` 403); that page needs only a session (`sessionGuard`), and the
  hub stays disconnected until the password is changed (ADR-0021).
- **HTTP.** `languageInterceptor` sends the app's `LOCALE_ID` as `Accept-Language`, so the
  API answers in the language on screen, not the browser's (ADR-0023). `jwtInterceptor`
  attaches `Authorization: Bearer` to requests for our own API
  only. `errorInterceptor` then turns every failure into an `ApiError` (status, title,
  detail, field errors from `ProblemDetails`).
  - A 401 from our API first renews the session once and retries with the new token: the
    session may well go on, e.g. after another tab changed the password. If it can't be
    renewed, the session ends and the user goes to login. The only exception is a request
    marked `ANONYMOUS_REQUEST` (login, renew), whose 401 just means a wrong password or no
    session.
  - Several requests failing together produce one redirect and one toast.
  - 0, 403, 404 and 5xx show a toast. 400 and 409 are left to the calling screen, because
    they carry form- or rule-specific messages. A caller that shows its own message sets
    `SKIP_ERROR_NOTIFICATION` on the request.
- **Languages.** Turkish and English from one build, Turkish by default (ADR-0025).
  `main.ts` loads the stored language's translations (`public/i18n/tr.json`) and locale data,
  then imports the app and bootstraps it with a matching `LOCALE_ID`. The **TR | EN** switch
  stores the choice and reloads. Texts are marked with `i18n` in templates and `$localize` in
  code; `npm run i18n:extract` lists what still needs a Turkish version.
- **Sidebar and user menu.** From 1100px the shell is a sidebar: the links, then the user's
  card with **TR | EN**, the theme switch and `<app-profile-menu>`; on narrower screens they sit
  in a bar on top. The menu shows the user's initials, name and role, and opens (upwards in the
  sidebar) a menu built to the WAI-ARIA menu button pattern (arrow keys, Escape, a click
  outside), with **My profile** (`/profile`, the user's own name), the password page and
  **Sign out**.
- **Password fields.** Every password input sits in `<app-password-field>`, whose eye button
  shows what was typed. It hides it again when the form is sent, so a password manager sees a
  password field.
- **Resource photos (ADR-0027).** `<app-resource-photo>` shows a resource's photo, or its
  type's picture from `public/images/resource-types` when it has none, in the resource list,
  the availability cards, the booking form and the resource form. `ResourcePhotoService`
  fetches a photo with the token and keeps an object URL per version (`photoUpdatedAt`). The
  resource form makes a chosen picture smaller (`shared/utils/photo.ts`) and uploads it after
  the details are saved.
- **Styling.** Plain SCSS. Design tokens (colors, the brand's gradients, spacing, radius,
  shadows) and shared building blocks (`.btn`, `.field`, `.input`, `.card`, `.alert-error`,
  `.brand-panel`, `.empty`) live in `src/styles.scss`. Component styles use those variables
  instead of hardcoded values. The dark theme redefines the colour tokens under
  `:root[data-theme='dark']`, which a script in `index.html` sets before the first paint: the
  stored choice, or the system's setting. The typeface (Plus Jakarta Sans) is bundled with the
  app from `@fontsource-variable`, so no page asks a font service for anything.

## 8. Team ownership boundaries

See [TEAM_SYNC.md](TEAM_SYNC.md) for who owns which module and how branches/PRs are
expected to flow between the two of us.
