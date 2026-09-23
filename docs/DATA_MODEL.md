# SmartDesk Data Model

## Entities

### ApplicationUser (`SmartDesk.Infrastructure.Identity`, extends `IdentityUser<Guid>`)

| Field | Type | Notes |
|---|---|---|
| Id | Guid | Identity's own key |
| FirstName | string | |
| LastName | string | |
| Email | string | Identity-managed, unique |
| (roles) | — | assigned via `AspNetUserRoles`, values from `Roles.Admin` / `Roles.Employee` |

### ResourceType

| Field | Type | Notes |
|---|---|---|
| Id | Guid | |
| Name | string | e.g. "Meeting Room", "Desk", "Company Car" |

### Resource

| Field | Type | Notes |
|---|---|---|
| Id | Guid | |
| Name | string | |
| ResourceTypeId | Guid | FK → ResourceType |
| Capacity | int | |
| Location | string | e.g. "2nd Floor" |
| Status | ResourceStatus | Active / Inactive / Maintenance |
| Description | string? | |

### ResourceFeature

| Field | Type | Notes |
|---|---|---|
| Id | Guid | |
| ResourceId | Guid | FK → Resource, cascade delete |
| Name | string | e.g. "TV", "Video Conference" |

### Reservation

| Field | Type | Notes |
|---|---|---|
| Id | Guid | |
| ResourceId | Guid | FK → Resource, cascade delete |
| UserId | Guid | references ApplicationUser.Id (no FK constraint across the Identity boundary by design - see below) |
| StartDateTime | DateTime | |
| EndDateTime | DateTime | must be > StartDateTime |
| Status | ReservationStatus | Confirmed / Cancelled |
| Note | string? | |

`Reservation.UserId` is stored as a plain `Guid`, not an EF navigation to `ApplicationUser`,
because `SmartDeskDbContext` treats Identity's own tables as belonging to the Identity
schema it manages; a direct FK is unnecessary since Identity guarantees the user row
exists at reservation-creation time, and the query direction we need (reservations for the
current user) is fully satisfiable filtering by a scalar `UserId`.

## Relationships

```
ResourceType 1 ── * Resource 1 ── * ResourceFeature
                          │
                          1
                          │
                          *
                    Reservation ── (UserId, scalar) ──→ ApplicationUser
```

## Indexes

- `Reservations(ResourceId, Status, StartDateTime, EndDateTime)` - composite index backing
  the overlap-check query every `CreateReservationCommand` will run (same resource, active
  reservations, does the new range intersect an existing one).

## Business rules (enforced in Application-layer handlers, backend-side - never frontend-only)

1. **No overlapping reservations for the same resource.** Two `Confirmed` reservations on
   the same `ResourceId` must not have intersecting `[StartDateTime, EndDateTime)` ranges.
   Example: existing `10:00–11:30`, new request `11:00–12:00` → rejected (ranges intersect).
2. `EndDateTime` must be strictly greater than `StartDateTime`.
3. A `Resource` with `Status` other than `Active` (`Inactive` or `Maintenance`) cannot be
   reserved.
4. A reservation's owner can cancel their own reservation; `Admin` can manage any
   reservation.
5. Concurrent requests for the same resource/time range must not both succeed - the
   overlap check has to hold under concurrent writes, not just sequentially (a transaction
   or a DB-level constraint, not only an in-handler read-then-write check with no
   protection between the read and the write).
6. A reservation cannot be created for a start time in the past.

## Open question for the team

Rule 5 (concurrency safety) is easiest to get exactly right with a Postgres exclusion
constraint (`EXCLUDE USING gist` over a `tstzrange`, requiring the `btree_gist` extension)
rather than an application-level check-then-insert, which has a race window between the
check and the write under concurrent requests. This isn't wired up yet - worth deciding
together before `CreateReservationCommand` is implemented, and recording the decision in
`docs/DECISIONS.md`.
