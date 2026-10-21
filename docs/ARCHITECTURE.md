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
| Logging | Serilog, to the console and to Seq (ADR-0053) |
| Local infra | Docker Compose: PostgreSQL, Mailpit and Seq; optionally the API, or the API and the app behind nginx (ADR-0031) |

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

Two more behaviors work around the booking handlers without being part of them. With a clash,
`BookingSuggestionsBehavior` (on create and update only) answers the 409 with what would do
instead (ADR-0042). Outermost, `OfferFreedSlotsBehavior` offers the times a request freed - noted
by `FreedSlotsInterceptor`, an EF Core SaveChanges interceptor - to the waiting list once the
request has succeeded (ADR-0043). Rules that depend on what a booking is for - how long, how far
ahead, office hours, how many per person - are applied in one place, `BookingPolicy`, which every
way of booking asks (ADR-0046).

A second interceptor, `AuditInterceptor`, keeps the history of changes: in every save, an
`AuditEntry` for each row it adds, changes or deletes - who, when, and each column before and
after - in the same transaction, so a change rolled back leaves nothing behind and no handler has
to remember it (ADR-0052).

Application code that needs something from outside (Identity, token signing, the current
HTTP caller) asks for it through an interface in `Application/Common/Interfaces`
(`IIdentityService`, `IJwtTokenGenerator`, `ICurrentUserService`); Infrastructure or API
provides the implementation. That keeps handlers unit-testable with plain mocks.

Exceptions carry no HTTP knowledge. `GlobalExceptionHandler` is the single place that maps
them to `ProblemDetails` responses: FluentValidation's `ValidationException` → 400,
`UnauthorizedException` → 401, `ForbiddenAccessException` → 403, `NotFoundException` →
404, `ConflictException` → 409, anything unexpected → 500 (logged).

Logs are Serilog's (ADR-0053): every `ILogger` message keeps its values as fields, on the
console and in Seq when `Seq:ServerUrl` is set. `LoggingSetup` adds a line per request, with
whose it was, and puts the user on every line written while a request runs; query strings,
bodies and headers are never written.

### 3.3 Backend project structure

```
src/
  SmartDesk.Domain/
    Entities/          Resource (its features, photo, teams, blocks, place on a plan),
                          ResourceType (with its booking rules), Reservation (attendees,
                          series), InboxMessage, IssueReport, FloorPlan, WaitlistEntry,
                          OfficeHours, AuditEntry
    Enums/              ResourceStatus, ReservationStatus, AccountStatus, AttendeeResponse,
                          InboxMessageKind, IssueCategory, IssueStatus, RecurrenceFrequency,
                          WeekDays, AuditAction, AuditActor
    Constants/          Roles (Admin, Manager, Employee)
    Rules/              ReservationOverlap and ResourceBlockOverlap - the one definition of
                          "time range taken"; CheckInRules - the check-in window and when a
                          booking is released; IssueRules
  SmartDesk.Application/
    Common/             Behaviors/ (ValidationBehavior), Exceptions/ (incl. ConflictCodes),
                          Extensions/, Interfaces/ (incl. IApplicationDbContext),
                          Localization/, Models/
    Features/<Area>/    Commands/<Name>/ and Queries/<Name>/ - request, handler,
                          validator and response DTO side by side; Common/ for what an area's
                          requests share (Reservations/Common: BookingGuard, BookingSuggester,
                          RecurrenceRules; BookingRules/Common: BookingPolicy, BookingLimits,
                          OfficeWeek)
    Resources/          Messages.resx, Messages.tr.resx: every message in English and Turkish
    DependencyInjection.cs  AddApplication(): MediatR + pipeline + validators
  SmartDesk.Infrastructure/
    Identity/           ApplicationUser, IdentityService, JwtTokenGenerator,
                          JwtOptions, sessions, IdentityDataSeeder
    Persistence/         SmartDeskDbContext, Migrations/, FreedSlotsInterceptor, Auditing/
                          (AuditInterceptor and what it keeps), Seed/ (resource types,
                          office hours, Development demo resources, DemoData/ for
                          `--demo-data`)
    Email/               the email queue, sent over SMTP or to the log
    Reports/             UsageReportWorkbook: the usage report as Excel (ClosedXML)
    Calendar/            IcalCalendarWriter: bookings as iCalendar files and feeds (Ical.Net)
    Assistant/           the booking assistant on Claude or Gemini
    DependencyInjection.cs  AddInfrastructure(): DbContext, Identity, options, services
  SmartDesk.API/
    Program.cs           composition root: JWT bearer, authorization, CORS,
                          OpenAPI/Swagger UI, exception handling, SignalR, startup seed,
                          `--demo-data`
    Controllers/          thin - build a command/query, send it through MediatR
    Hubs/                 ReservationHub, and each user's open connections
                          (HubConnectionRegistry), cut when their account changes
    Services/             CurrentUserService (reads the caller from the JWT); the SignalR
                          notifiers for reservations, the inbox and problem reports (the Null*
                          ones remain only as unused fallbacks); and the timed jobs, each
                          sending one command every 30 seconds (PeriodicCommandWorker):
                          reminders, the warning before a release, releasing no-shows, and
                          asking whether a booking about to end needs longer
    OpenApi/              Bearer security scheme for Swagger UI
    Middleware/           GlobalExceptionHandler
    Logging/              LoggingSetup: Serilog, a line per request, the user on every line
tests/
  SmartDesk.Tests/        unit tests for Application handlers and Domain rules;
                          handlers that query run against SmartDeskDbContext on
                          in-memory SQLite (TestSupport/SqliteTestDatabase)
  SmartDesk.IntegrationTests/  the API over HTTP, on PostgreSQL in a container
                          (Testcontainers): the races, the exclusion constraint, the locks
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

Two events go to the reservation's owner alone: the reminder (`ReservationStartingSoon`,
ADR-0018) and the notice that a booking was released because nobody checked in
(`ReservationReleased`, ADR-0030), and the question whether a booking about to end needs longer
(`ReservationEndingSoon`, ADR-0033). The warning before a release goes to the owner and those
invited (`ReservationReleaseWarning`, ADR-0034). They come from timed jobs rather than a request.

Inbox messages (ADR-0032) go to their recipient alone, through `IInboxNotifier`
(`SignalRInboxNotifier`, `InboxMessageReceived`). Handlers write them with the change, in the
same transaction (`MeetingInvitations`), and push them only after it's committed, so a message
never announces a change that rolled back.

What reaches one person goes to their phones and browsers too, the app open or not (ADR-0056).
The API's `ReservationNotifiers` and `InboxNotifiers` hand each event to the SignalR notifier and
to `PushReservationNotifier` or `PushInboxNotifier`, which put a `PushNotice` on the queue behind
`IPushSender`. `PushDispatchWorker` writes each notice for every device of its recipients - in the
owner's language, with the office's time zone (`PushTexts`) - encrypts it for the device
(`WebPushEncryption`, RFC 8291), signs a VAPID token (`VapidAuthorization`, RFC 8292) and posts it
to the device's push service, forgetting a device the service no longer knows. Without the
`WebPush` keys nothing is queued.

The Angular app opens the connection when a session exists and closes it on logout.
Negotiate does not send cookies (`withCredentials: false`); the hub uses the bearer token,
renewed first if it's about to expire, on every (re)connect.
A failed start and a dropped connection both retry forever, at 0s, 2s, 5s, 10s, then
every 30s. Logout cancels a pending retry. Screens subscribe to `created$`, `updated$`,
`cancelled$` and `reconnected$` on `ReservationHubService` rather than the raw connection.

### 6.1 The booking assistant (optional)

ADR-0029. `POST /api/assistant/messages` → `AskAssistantCommand` → `IAssistantModel`, which
Application defines and Infrastructure implements with Claude (`ClaudeAssistantModel`,
Anthropic's SDK). One answer is a loop: the model replies, or asks for tools. The
`AssistantToolbox` runs them, and their results go back until it replies or runs out of rounds
(`Assistant:MaxToolRounds`).

The toolbox only sends MediatR queries, in the request's scope and so as the signed-in user:
`GetAvailableResourcesQuery`, `GetResourcesQuery`, `GetResourceTypesQuery`,
`GetMyReservationsQuery` and `GetResourceByIdQuery`. The two `propose_*` tools add a card to
the answer and nothing else. The frontend's `Assistant` component (in the shell) shows the
conversation, and a card's button calls `ReservationService` like any screen. Without
`Assistant:ApiKey` the model reports itself not configured, and the app shows nothing.

## 7. Frontend architecture

Angular 22, standalone components throughout (no NgModules), feature-based folders under
`frontend/src/app/features/`, one folder per screen: the MVP list (login, dashboard,
resources, availability, reservation, my-reservations, admin) plus home (the public front
page), calendar, reports and resource-blocks, and since then floor-plan, booking-rules, inbox,
issues, check-in, door-screen, door-visit, qr-labels, team, users, profile, guide,
approvals and history. Shared, cross-feature code (HTTP interceptor, auth guard, typed API clients) lives
under `frontend/src/app/core/`.
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
  `/admin/blocks`, `/admin/rules`). `managerGuard` protects `/team`. `resourceEditorGuard` lets admins and
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
- **Check-in at the door (ADR-0030).** A resource's QR code leads to `/check-in/:resourceId?code=…`
  inside the shell, so a signed-out phone signs in first and comes back with the code. The page
  shows the caller's booking of that resource and a **Check in** button while it's open, and
  follows the clock and live events. The resource form switches check-in on and shows the code
  (`<app-check-in-code>`, drawn by `<app-qr-code>` from `qrcode-generator`), and
  `/resources/:id/check-in-poster`, outside the shell, is the sheet to print. The booking form
  and **My reservations** say when to check in, and `ReminderAlertsService` tells the owner when
  a booking was released.
- **Meetings and the inbox (ADR-0032).** The booking form lists who the user may invite
  (`GET /api/reservations/invitees`), with what each answered. `InboxService`, started by the
  app, keeps the unread count for the **Inbox** link and the narrow bar's bell, and shows each
  new message as a toast (and a desktop notification when allowed). `/inbox` lists the messages
  and lets an invitation be answered there (`<app-meeting-answer>`, also in **My reservations**
  and on the meeting's page). `core/i18n/inbox-texts.ts` writes each message's sentence.
- **Running over (ADR-0033).** `<app-ending-soon>` in the shell shows the question when
  `ReservationEndingSoon` arrives: a card in the corner with `<app-extend-booking>`, the
  extension options that still fit as buttons. One booking at a time; one extended or cancelled
  elsewhere leaves by itself. **My reservations** has the same buttons behind **Extend** for a
  booking in progress.
- **The door screen (ADR-0034).** `/door/:resourceId`, outside the shell and without a user:
  it keeps the token from its link (in `localStorage`, out of the address), asks
  `GET /api/door/{id}` through `DoorScreenService` just after its code changes, and draws the
  room's state (`doorState`) and the QR code. The resource form's `<app-check-in-code>` sets
  the screen up. The check-in page reads the code once and drops it from the address, and
  offers what can be done at the door: run longer, end, or book on the spot.
- **The page without an account (ADR-0050).** While nobody is signed in, `visitorGuard` (a
  `canMatch`) gives `/check-in/:resourceId` a page of its own outside the shell, `DoorVisit`. It
  asks `GET /api/door/{id}/visit` with the code in a header through `DoorVisitService`, draws
  the room's state with the door screen's `doorState`, checks in the booking waiting for it, and
  reports a problem with `<app-report-issue [doorCode]>`. Signed in, the router goes on to the
  shell's own `check-in/:resourceId`, so the printed codes keep their address.
- **Favourites (ADR-0051).** `FavoriteService` (in root) keeps the signed-in user's starred
  resources, loaded once for whoever is signed in and changed on screen before the API answers;
  `<app-favorite-toggle>` is the star wherever a resource is listed. The dashboard's **Your
  favourites** works out each one's state with `roomNowFor`, from today's bookings it already
  has and today's blocked windows, asked once a day.
- **History (ADR-0052).** `/admin/history` (`AuditHistory`) asks `GET /api/audit` through
  `AuditService` a page at a time, with its filters in a reactive form and a resource's id from
  the address (`?resourceId=`, the link on the resource form). `audit-text.ts` says each entry
  in words - what and which, who, each column before and after - with the names the page brings
  and the kinds of resource, in the screen's language and time zone.
- **Installed (ADR-0054).** `provideServiceWorker` in a production build, with `ngsw-config.json`
  saying what it keeps - the app's own files, never the API's answers - and
  `manifest.webmanifest`. `AppUpdateService` offers a new version once the worker has it (a door
  screen just reloads), `ConnectivityService` follows the browser going offline and back, and
  the notification host shows both above its toasts.
- **Notifications (ADR-0056).** `PushService` (in root) over Angular's `SwPush`, so only in a
  production build: `refresh()` works out where the device stands - no Push API, Safari on an
  iPhone outside its Home Screen app, a server without keys, blocked, off or on - and hands a
  subscription found on to the API again; `turnOn()` asks the browser's permission in the click
  itself, then subscribes with the server's key. The profile's `<app-push-settings>` shows it,
  and the account menu's sign-out awaits `forgetThisDevice()` first, a few seconds at most. The
  service worker shows what arrives and opens the app on the notification's URL.
- **On a phone (ADR-0055).** Up to 640px the shell adds `<app-tab-bar>` (`layout/tab-bar`), a
  bar at the bottom with four screens and **Menu**, a sheet with every screen in the shell's
  `visibleNavItems` (`NavItem`, `layout/nav-item.ts`), and hides its own strip of links. While
  it's there, `has-tab-bar` on `<body>` sets `--tab-bar-space`, which whatever is fixed at the
  bottom (toasts, the assistant, the ending-soon card) and the page's own end add.
  `ThemeService` keeps `<meta name="theme-color">` the top bar's colour in either theme.
- **The guide.** `/guide` lists `GUIDE_SECTIONS` (`features/guide/guide-sections.ts`): each
  part's steps as `$localize` texts, the numbers in them taken from the rules' constants
  (`CHECK_IN`, `EXTENSION_OPTIONS`), filtered by role and by whether the server has the
  assistant. The contents on top link to each part's fragment; the page scrolls there and moves
  the focus to its heading itself, without the router's anchor scrolling.
- **Bookings that repeat (ADR-0035).** The booking form's Repeat section is part of its
  reactive form (`repeat`, `repeatDays`, `until`). From them it works out the rule
  (`recurrence`), asks `GET /api/reservations/series/preview` as the form changes - after a
  pause (`SERIES_PREVIEW_DEBOUNCE`), the newest answer only - and lists the dates with
  `<app-series-dates>`. `shared/utils/recurrence.ts` says a rule in the reader's words ("every
  Monday until 2 Nov 2026"); My reservations and the inbox use it too.
- **Styling.** Plain SCSS. Design tokens (colors, the brand's gradients, spacing, radius,
  shadows) and shared building blocks (`.btn`, `.field`, `.input`, `.card`, `.alert-error`,
  `.brand-panel`, `.empty`) live in `src/styles.scss`. Component styles use those variables
  instead of hardcoded values. The dark theme redefines the colour tokens under
  `:root[data-theme='dark']`, which a script in `index.html` sets before the first paint: the
  stored choice, or the system's setting. The typeface (Plus Jakarta Sans) is bundled with the
  app from `@fontsource-variable`, so no page asks a font service for anything.

## 8. What the ten-day plan added, and why

After the MVP, a ten-day internship plan added a feature a day, on the same layers and by the
same rules. The decisions themselves are in [DECISIONS.md](DECISIONS.md); this is where each
day sits and why it took the shape it has. Where the plan suggested a tool and the code went
another way, the reason is here too.

1. **A safety net: CI and a concurrency test.** GitHub Actions on every push - backend build,
   tests and a check that the model has its migration; frontend build, tests, translations and
   formatting; the browser tests; the whole app in containers. Integration tests run the API
   over HTTP on a real PostgreSQL in a container (Testcontainers, `PostgresApiFixture`), among
   them a race that books one slot many times at once. Behind the handlers' own check, a
   PostgreSQL exclusion constraint (`btree_gist`, `AddReservationOverlapConstraint`) makes an
   overlap impossible, and its violation comes back as the same 409. *Why:* every later change
   is checked before it lands, and "two requests for one slot" is proven on the database that
   runs it rather than assumed (ADR-0011).
2. **An inbox and reminders.** Messages are written in the same transaction as the change
   (`MeetingInvitations`) and pushed over SignalR to their recipient only once it's committed;
   reminders go out from a timed job; email is queued and sent in the background. *Why:* the
   later days - check-in, blocks, the waiting list - all need to tell someone something. The
   jobs are hosted services that send one MediatR command every 30 seconds
   (`PeriodicCommandWorker`) rather than a job library such as Hangfire: the rule stays in a
   handler, tested like any other, and nothing needs a store of its own (ADR-0018, ADR-0019,
   ADR-0032).
3. **QR check-in and no-shows.** Each resource's QR code opens the check-in page; a booking
   nobody checks in to within the grace (`CheckIn:GraceMinutes`) is released, its people warned
   first and its owner told. *Why:* rooms booked and not used go back to people who are there
   (ADR-0030, ADR-0040).
4. **Bookings that repeat.** A series is a rule, and each date a reservation of its own
   (`ReservationSeries`, `RecurrenceRules`), shown before it's booked. *Why:* one date can be
   moved, checked in or cancelled alone, with every rule a booking has (ADR-0035).
5. **Rules and maintenance.** Blocked windows, which can take their window's bookings with them
   when asked; and the rules an Admin sets for each kind of resource and the office's hours,
   applied by `BookingPolicy` (`/admin/rules`). *Why:* one policy that every way of booking
   asks, rather than the same checks spread over the handlers (ADR-0015, ADR-0038, ADR-0046).
6. **Suggestions and a waiting list.** A clash comes back with the nearest free times and other
   resources free then; a freed time is booked for whoever waited for it first. *Why:* both work
   around the handlers - a pipeline behavior after a failed booking, an interceptor and a
   notification after a successful one - so they never hold a lock or roll back the booking
   that set them off (ADR-0042, ADR-0043).
7. **The floor plan.** A picture per floor - the location its resources already share - with
   the resources on it by their fractions of its width and height, coloured by their state at
   any time and changing live. *Why:* the plan suggested an SVG drawn per office and Angular
   CDK drag and drop; an uploaded picture fits any office without drawing, and placing works by
   pointer and by keyboard with no extra dependency (ADR-0041).
8. **Reports.** Utilization, a weekday-by-hour heat map, no-show and cancellation rates and the
   busiest people, and the same as an Excel workbook (ClosedXML). *Why:* computed in memory over
   at most 92 days rather than with `GroupBy` in SQL as the plan suggested, which keeps the time
   zone and office-hour clipping in plain, tested C#; the heat map is an accessible table rather
   than a chart library (ADR-0017, ADR-0044, ADR-0045).
9. **A door screen.** `/door/:resourceId` shows free or taken, until when, what's next, and a QR
   code that changes every 30 seconds; checking in with it proves you're at the door. *Why:* a
   printed code can be photographed. The screen has a token of its own, kept only as a hash,
   and asks the API just after its code changes rather than joining SignalR, since it has no
   user (ADR-0034, ADR-0039).
10. **Getting ready for the demo.** `--demo-data` fills a fresh Development database with an
    office's worth of people and six weeks of bookings, with Bogus (ADR-0047); this section;
    and [DEMO_UI.md](DEMO_UI.md), the walkthrough.

Besides the plan, the same months brought accounts and teams (ADR-0020 - ADR-0022), Turkish and
English throughout (ADR-0023, ADR-0025), sessions renewed from an HttpOnly cookie (ADR-0026),
resource photos (ADR-0027), the booking assistant (ADR-0029, ADR-0036), meetings (ADR-0032),
running over (ADR-0033), problems reported at the door (ADR-0037) and the door's page without
an account (ADR-0050). From the plan's backup pool came bookings in a calendar app: a file per
booking and a personal link to subscribe to (ADR-0048); bookings that wait for an Admin's
approval, which hold their time meanwhile (ADR-0049); favourite resources (ADR-0051); a
history of every change for Admins, written by an interceptor in the same save (ADR-0052);
logs to query in Seq, a line per request with whose it was (ADR-0053); an app to install,
which opens without a network (ADR-0054); on a phone, the main screens in a bar at the bottom,
where a thumb reaches (ADR-0055); and notifications on phones and browsers with the app closed,
which the API encrypts and sends itself (ADR-0056).

## 9. Team ownership boundaries

See [TEAM_SYNC.md](TEAM_SYNC.md) for who owns which module and how branches/PRs are
expected to flow between the two of us.
