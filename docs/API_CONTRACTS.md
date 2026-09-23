# SmartDesk API Contracts

Planned HTTP surface, written ahead of implementation so frontend work can start against a
stable contract. Update this file in the same PR as any endpoint you add, rename, or
change the shape of - it is the source of truth for what the API looks like, not the
controller code.

All endpoints are under `/api`. Authenticated endpoints expect `Authorization: Bearer
<jwt>`. Error responses use `application/problem+json` (RFC 7807 `ProblemDetails`).

## Auth

| Method | Path | Auth | Notes |
|---|---|---|---|
| POST | `/api/auth/login` | none | body: `{ email, password }` → `{ accessToken, expiresAt }` |
| GET | `/api/auth/me` | any authenticated user | current user's id, name, email, roles |

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
