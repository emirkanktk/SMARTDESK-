# SmartDesk API Contracts

Planned HTTP surface, written ahead of implementation so frontend work can start against a
stable contract. Update this file in the same PR as any endpoint you add, rename, or
change the shape of - it is the source of truth for what the API looks like, not the
controller code.

All endpoints are under `/api`. Authenticated endpoints expect `Authorization: Bearer
<jwt>`. Error responses use `application/problem+json` (RFC 7807 `ProblemDetails`).

## Auth

| Method | Path | Auth | Command/Query | Notes |
|---|---|---|---|---|
| POST | `/api/auth/login` | none | `LoginCommand` | body: `{ email, password }` → `{ accessToken, expiresAt }`. 400 if a field is missing/malformed, 401 on any other failure. |
| GET | `/api/auth/me` | any authenticated user | `GetCurrentUserQuery` | → `{ id, firstName, lastName, email, roles: string[] }` |

- Login failures all return the same 401 `"Invalid email or password."` - unknown email,
  wrong password and locked-out account are deliberately indistinguishable. Five failed
  attempts lock the account for five minutes (ASP.NET Core Identity defaults).
- The access token is valid for `Jwt:AccessTokenLifetimeMinutes` (60 by default); there is no
  refresh token yet - on a 401 the frontend sends the user back to login.
- Token claims: `sub` (user id), `email`, `given_name`, `family_name`, one `role` claim per
  role (`Admin` / `Employee`).

## Resource types

| Method | Path | Auth | Notes |
|---|---|---|---|
| GET | `/api/resource-types` | any authenticated user | flat list, for populating forms/filters |

## Resources

| Method | Path | Auth | Command/Query | Notes |
|---|---|---|---|---|
| GET | `/api/resources` | any authenticated user | — | full list with filtering (type, status) for the Resources screen |
| GET | `/api/resources/available` | any authenticated user | `GetAvailableResourcesQuery` | query params: `startDateTime`, `endDateTime`, optional `resourceTypeId`, `minCapacity` |
| POST | `/api/resources` | Admin | `CreateResourceCommand` | body: name, resourceTypeId, capacity, location, description, features |
| PUT | `/api/resources/{id}` | Admin | `UpdateResourceCommand` | full replace of the mutable fields above, plus `status` |

## Reservations

| Method | Path | Auth | Command/Query | Notes |
|---|---|---|---|---|
| POST | `/api/reservations` | any authenticated user | `CreateReservationCommand` | body: resourceId, startDateTime, endDateTime, note. 409 on overlap. |
| POST | `/api/reservations/{id}/cancel` | owner or Admin | `CancelReservationCommand` | idempotent - cancelling an already-cancelled reservation is a no-op, not an error |
| GET | `/api/reservations/mine` | any authenticated user | `GetMyReservationsQuery` | current user's reservations, active + past |

## Dashboard

| Method | Path | Auth | Command/Query | Notes |
|---|---|---|---|---|
| GET | `/api/dashboard` | any authenticated user | `GetDashboardQuery` | today's reservations, resources currently in use, upcoming reservations |

## Real-time (SignalR)

| Hub | Path | Events |
|---|---|---|
| `ReservationHub` | `/hubs/reservations` | `ReservationCreated`, `ReservationCancelled` - both carry enough of the reservation's shape for an open calendar/dashboard to patch its view without a refetch |

## Conventions

- Timestamps are UTC ISO-8601 over the wire; the frontend converts to local time for
  display.
- List endpoints that can grow (reservations, resources) should be paginated once real
  data volume shows up - not needed for the MVP's expected data size.
- DTOs are never the EF entities directly (see `docs/DECISIONS.md`, ADR-0002).
- Every endpoint requires an authenticated user unless it's explicitly marked anonymous
  (ADR-0008) - "none" in an Auth column above means `[AllowAnonymous]`.
- Error status codes: 400 validation (`errors` holds a field → messages map), 401 missing/
  invalid token or failed login, 403 authenticated but not allowed (e.g. non-Admin), 409
  business-rule conflict (e.g. overlapping reservation), 500 unexpected. Validation messages
  are always English, whatever the server's locale.
