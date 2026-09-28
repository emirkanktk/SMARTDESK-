# SmartDesk Data Model

## Entities

### ApplicationUser (`SmartDesk.Infrastructure.Identity`, extends `IdentityUser<Guid>`)

| Field | Type | Notes |
|---|---|---|
| Id | Guid | Identity's own key |
| FirstName | string | |
| LastName | string | |
| Email | string | Identity-managed, unique |
| Status | AccountStatus | `Active` (0), `PendingApproval` (1), `Disabled` (2). Only `Active` accounts can sign in or use a token (ADR-0020). Existing accounts became `Active`. |
| ManagerId | Guid? | FK → `ApplicationUser.Id` (restrict delete). Set only for an `Employee` in a Manager's team. |
| CreatedAt | DateTime (UTC) | when the account was made; existing accounts got the migration's time |
| MustChangePassword | bool | the password was chosen by an Admin or Manager; the owner must replace it before anything else (ADR-0021) |
| (roles) | — | assigned via `AspNetUserRoles`, one of `Roles.Admin` / `Roles.Manager` / `Roles.Employee` |

### ResourceType

| Field | Type | Notes |
|---|---|---|
| Id | Guid | |
| Name | string | unique. Seeded by migration: Meeting Room, Desk, Company Car, Projector, Camera |

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
| ReminderMinutes | int? | remind the owner this many minutes before the start: 5, 15, 30 or 60; null = no reminder (ADR-0018) |
| ReminderSentAt | DateTime? | when that reminder went out; cleared when the start or `ReminderMinutes` changes. A partial index on `StartDateTime` covers the rows still waiting for one |

`Reservation.UserId` is stored as a plain `Guid`, not an EF navigation to `ApplicationUser`,
because `SmartDeskDbContext` treats Identity's own tables as belonging to the Identity
schema it manages; a direct FK is unnecessary since Identity guarantees the user row
exists at reservation-creation time, and the query direction we need (reservations for the
current user) is fully satisfiable filtering by a scalar `UserId`.

### ResourceBlock

| Field | Type | Notes |
|---|---|---|
| Id | Guid | |
| ResourceId | Guid | FK → Resource, cascade delete |
| StartDateTime | DateTime | UTC |
| EndDateTime | DateTime | must be > StartDateTime |
| Reason | string | required, max 200 - shown to anyone whose booking the block refuses |
| CreatedByUserId | Guid | the admin who created it (scalar, like `Reservation.UserId`) |

## Relationships

```
ResourceType 1 ── * Resource 1 ── * ResourceFeature
                          │
                          1
                          │
                          *
                    Reservation ── (UserId, scalar) ──→ ApplicationUser

Resource 1 ── * ResourceBlock

ApplicationUser (Manager) 1 ── * ApplicationUser (Employee, via ManagerId)
```

## Indexes

- `AspNetUsers(ManagerId)` - a Manager's team.
- `ResourceTypes(Name)` - unique.
- `ResourceBlocks(ResourceId, StartDateTime, EndDateTime)` - the "is this resource blocked for
  `[start, end)`?" check on every booking, and the calendar's range query.
- `Reservations(ResourceId, Status, StartDateTime, EndDateTime)` - composite index backing
  the overlap-check query every `CreateReservationCommand` runs (same resource, active
  reservations, does the new range intersect an existing one).

## Constraints

- `CK_Reservations_EndAfterStart` - `CHECK ("EndDateTime" > "StartDateTime")` (rule 2).
- `CK_ResourceBlocks_EndAfterStart` - the same check for blocks.
- `EX_Reservations_NoOverlap` - `EXCLUDE USING gist ("ResourceId" WITH =,
  tstzrange("StartDateTime", "EndDateTime", '[)') WITH &&) WHERE ("Status" = 0)`: no two
  `Confirmed` reservations of one resource may overlap (rule 1). The range is half-open,
  like `ReservationOverlap`. Requires the `btree_gist` extension; both are created in raw
  SQL by the `AddReservationOverlapConstraint` migration, since EF Core can't model an
  exclusion constraint.

## Business rules (enforced in Application-layer handlers, backend-side - never frontend-only)

1. **No overlapping reservations for the same resource.** Two `Confirmed` reservations on
   the same `ResourceId` must not have intersecting `[StartDateTime, EndDateTime)` ranges.
   Example: existing `10:00–11:30`, new request `11:00–12:00` → rejected (ranges intersect).
   Ranges are half-open, so `10:00–11:00` and `11:00–12:00` do not overlap. The single
   definition lives in `SmartDesk.Domain.Rules.ReservationOverlap`, used by both the
   availability search and reservation creation.
2. `EndDateTime` must be strictly greater than `StartDateTime`.
3. A `Resource` with `Status` other than `Active` (`Inactive` or `Maintenance`) cannot be
   reserved. Changing a resource to `Inactive`/`Maintenance` stops new bookings but leaves
   its existing reservations in place; an admin cancels those individually if needed.
4. A reservation's owner can cancel or change their own reservation; `Admin` can manage any
   reservation. Cancelling is idempotent, and a reservation that has already ended can't
   be cancelled or changed. Once a reservation has started, only its end time and note can
   change; moving it in time or to another resource is checked like a new booking (rules
   1, 3, 6), ignoring its own current slot.
5. Concurrent requests for the same resource/time range must not both succeed - the
   overlap check has to hold under concurrent writes, not just sequentially. Enforced in
   two layers (ADR-0011): `CreateReservationCommand` locks the resource row for the
   duration of its transaction, so bookings of one resource run one at a time; and the
   `EX_Reservations_NoOverlap` exclusion constraint makes the database itself reject an
   overlapping `Confirmed` row, whatever wrote it.
6. A reservation cannot be created for a start time in the past (5 minutes of grace, so
   booking "from now" isn't rejected by clock drift). A reservation lasts at most 31 days.
7. A resource can't be booked, or a reservation moved, into a window covered by a
   `ResourceBlock`. A block can't be created over `Confirmed` reservations or over another
   block of the same resource. Creating a block takes the same per-resource lock as booking,
   so a block and a reservation can never both claim one slot (ADR-0015).
8. **Accounts (ADR-0020).** Each account has one role. Only an `Employee` has a manager, and
   that manager is an active `Manager`. A Manager manages only their own team; an Admin
   manages everyone. Nobody changes their own role or disables themselves. There is always
   at least one active Admin. A Manager who still has team members can't be given another
   role. Only a sign-up waiting for approval is ever deleted (by rejecting it); any other
   account is disabled instead, which keeps its reservations and history.

## Concurrency (formerly an open question)

Settled in `docs/DECISIONS.md`, ADR-0011: an exclusion constraint *and* a per-resource lock -
see rule 5 above and the Constraints section.
