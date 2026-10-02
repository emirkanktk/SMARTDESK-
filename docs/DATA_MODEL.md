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
| Language | string? (max 8) | `en` or `tr`: the language the user last signed in with, which their emails are written in. Null for an account that hasn't signed in since the column was added (ADR-0023). |
| (roles) | — | assigned via `AspNetUserRoles`, one of `Roles.Admin` / `Roles.Manager` / `Roles.Employee` |

### UserSession (`SmartDesk.Infrastructure.Identity`, ADR-0026)

A sign-in session. The refresh token itself is never stored.

| Field | Type | Notes |
|---|---|---|
| Id | Guid | the session id, in every access token issued under it (`sess`) |
| UserId | Guid | FK → `ApplicationUser.Id` (cascade delete) |
| TokenHash | string (64) | SHA-256 of the current refresh token, hex; unique |
| PreviousTokenHash | string? (64) | the one the last renewal replaced, accepted for `Sessions:ReuseGraceSeconds` after `RotatedAt` |
| RotatedAt | DateTime? (UTC) | when the refresh token was last replaced |
| SessionStamp | string (32) | the account's security-stamp digest when the session began; a password change ends it |
| CreatedAt | DateTime (UTC) | |
| ExpiresAt | DateTime (UTC) | moves forward with every renewal (`Sessions:IdleDays`), up to `AbsoluteExpiresAt` |
| AbsoluteExpiresAt | DateTime (UTC) | `CreatedAt` + `Sessions:MaxDays` |
| RevokedAt | DateTime? (UTC) | set on sign-out, on a password change, or when a replaced token is used again |

A user's sessions that ended or expired more than `Sessions:MaxDays` ago are deleted when
they next sign in.

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
| CheckInRequired | bool | bookings must be checked in at the door with its QR code, or they're released (ADR-0030) |
| CheckInCode | string | the secret in its QR code: 32 hex digits, random, required. Only those who may edit the resource see it; replacing it retires the printed code |
| DoorScreenTokenHash | string? (64) | SHA-256 of the door screen's token, as hex; while set, only the screen's changing code checks in (ADR-0034) |
| DoorScreenSetUpAt | DateTime? | when that screen was set up |

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
| CheckInRequired | bool | whether its resource asked for a check-in when it was booked, or last moved to a new start or another resource (ADR-0030) |
| CheckedInAt | DateTime? | when the owner checked in; cleared when the start or the resource changes |
| ReleasedAt | DateTime? | when it was released because nobody checked in; `Status` is then `Cancelled` |
| EndingSoonSentAt | DateTime? | when its owner was asked whether it needs longer; cleared when the end moves (ADR-0033) |
| CheckedInByUserId | Guid? | who checked it in: the owner or someone invited; cleared with `CheckedInAt` (ADR-0034) |
| ReleaseWarningSentAt | DateTime? | when the owner and those invited were warned, five minutes before the release (ADR-0034) |
| SeriesId | Guid? | FK → `ReservationSeries.Id` (restrict), indexed: the repeating booking it's one date of (ADR-0035) |

`Reservation.UserId` is stored as a plain `Guid`, not an EF navigation to `ApplicationUser`,
because `SmartDeskDbContext` treats Identity's own tables as belonging to the Identity
schema it manages; a direct FK is unnecessary since Identity guarantees the user row
exists at reservation-creation time, and the query direction we need (reservations for the
current user) is fully satisfiable filtering by a scalar `UserId`.

### ReservationSeries

A booking that repeats (ADR-0035): the rule it was made with, never edited. Each date is a
`Reservation` pointing here.

| Field | Type | Notes |
|---|---|---|
| Id | Guid | |
| UserId | Guid | who made it, the owner of every date; no FK across the Identity boundary |
| Frequency | RecurrenceFrequency | Daily / Weekly |
| Interval | int | every 1 to 4 days or weeks (check constraint) |
| DaysOfWeek | WeekDays | the days of a weekly series, as flags (Monday = 1 ... Sunday = 64); none for a daily one |
| Until | DateOnly | the last day it may come back on, inclusive, in its time zone |
| TimeZone | string (100) | IANA id the dates were worked out in |
| CreatedAt | DateTime | |

### ReservationAttendee

Someone invited to a booking (ADR-0032). Key (`ReservationId`, `UserId`).

| Field | Type | Notes |
|---|---|---|
| ReservationId | Guid | FK → `Reservation.Id`, cascade delete |
| UserId | Guid | an account id; no FK across the Identity boundary. Indexed, for "the meetings I'm invited to" |
| Response | AttendeeResponse | Pending / Accepted / Declined; back to Pending when the meeting gets a new start or another resource |
| RespondedAt | DateTime? | when they answered |
| DeclineReason | string? (500) | why they can't come; for the organizer only, never in a reservation response |

### InboxMessage

A message in someone's inbox (ADR-0032). It keeps its own copy of the meeting, so it outlives
the booking it's about.

| Field | Type | Notes |
|---|---|---|
| Id | Guid | version 7, so ids sort like `CreatedAt` |
| RecipientId | Guid | an account id; no FK across the Identity boundary |
| Kind | InboxMessageKind | MeetingInvitation / MeetingChanged / MeetingCancelled / MeetingUninvited / MeetingAccepted / MeetingDeclined / MeetingExtended (ADR-0033) / SeriesInvitation / SeriesCancelled / SeriesAccepted / SeriesDeclined (ADR-0035) |
| SenderId | Guid? | who caused it; null for a release |
| ReservationId | Guid? | FK → `Reservation.Id`, set null when the booking is deleted |
| ResourceName | string (200) | the meeting when the message was sent: the room's name, |
| StartDateTime, EndDateTime | DateTime | its times, |
| Note | string? (500) | and the organizer's note |
| Reason | string? (500) | why someone can't come, in a MeetingDeclined or SeriesDeclined |
| SeriesId | Guid? | FK → `ReservationSeries.Id` (set null), for a Series* message: which series; its times are then the first date it covers |
| CreatedAt | DateTime | |
| ReadAt | DateTime? | null while unread |

### ResourceTeam

A team a resource belongs to (ADR-0022). Key (`ResourceId`, `TeamId`).

| Field | Type | Notes |
|---|---|---|
| ResourceId | Guid | FK → `Resource.Id`, cascade delete |
| TeamId | Guid | the team's Manager account id; no FK across the Identity boundary. Indexed |

A resource with no rows here is shared by everyone.

### ResourcePhoto

A resource's photo (ADR-0027). At most one per resource, keyed by it.

| Field | Type | Notes |
|---|---|---|
| ResourceId | Guid | key, and FK → `Resource.Id`, cascade delete |
| ContentType | string | `image/jpeg`, `image/png` or `image/webp`, recognised from the first bytes when it was uploaded; max 32 |
| Data | byte[] | the image, at most 2 MB (`bytea`) |
| UpdatedAt | DateTime | UTC, when it was last set. Resource responses carry it as `photoUpdatedAt`, and the app uses it to tell a new photo from one it already has |

A resource without a row here shows its type's picture, which ships with the app.

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

Resource 1 ── 0..1 ResourcePhoto

ApplicationUser (Manager) 1 ── * ApplicationUser (Employee, via ManagerId)

Resource * ── * team (ResourceTeam.TeamId = a Manager's id; none = shared)

Reservation 1 ── * ReservationAttendee ── (UserId, scalar) ──→ ApplicationUser

InboxMessage ── (RecipientId, SenderId, scalar) ──→ ApplicationUser
InboxMessage * ── 0..1 Reservation (set null when it's deleted)

ReservationSeries 1 ── * Reservation (its dates)
ReservationSeries 1 ── * InboxMessage (Series* messages)
```

## Indexes

- `AspNetUsers(ManagerId)` - a Manager's team.
- `UserSessions(TokenHash)` - unique; `UserSessions(PreviousTokenHash)`, `UserSessions(UserId)` -
  finding the session a refresh token belongs to, and a user's open sessions.
- `ResourceTypes(Name)` - unique.
- `ResourceBlocks(ResourceId, StartDateTime, EndDateTime)` - the "is this resource blocked for
  `[start, end)`?" check on every booking, and the calendar's range query.
- `Reservations(ResourceId, Status, StartDateTime, EndDateTime)` - composite index backing
  the overlap-check query every `CreateReservationCommand` runs (same resource, active
  reservations, does the new range intersect an existing one).
- `IX_Reservations_PendingReminders` on `Reservations(StartDateTime)`, only rows with a reminder
  still to send - what the reminder job scans every 30 seconds.
- `IX_Reservations_AwaitingCheckIn` on `Reservations(StartDateTime)`, only `Confirmed` rows that
  need a check-in nobody has made yet - what the release job scans every 30 seconds (ADR-0030).
- `ReservationAttendees(UserId)` - the meetings someone is invited to (ADR-0032).
- `InboxMessages(RecipientId, CreatedAt)` - someone's inbox, newest first; and
  `IX_InboxMessages_Unread` on `InboxMessages(RecipientId)`, only unread rows - the count the app
  asks for on every page.

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
9. **Team resources (ADR-0022).** A resource with teams may be seen, booked and blocked only by
   those teams' members and managers, and by Admins; for anyone else it doesn't exist (404).
   A resource with no teams is shared by everyone. A Manager manages only resources that
   belong to their team alone. A Manager whose team has resources keeps the role until they're
   shared or given to another team.
10. **Check-in (ADR-0030).** A booking of a resource with `CheckInRequired` is checked in by its
    owner at the door, from 10 minutes before its start until 15 minutes after (never once it's
    over), with the resource's current `CheckInCode`. One nobody checked in to by then is
    released: `Cancelled`, with `ReleasedAt`.
    - This applies only when the booking's own `CheckInRequired` and its resource's are both
      set, so switching check-in on doesn't catch bookings made without knowing, and switching
      it off frees them all.
    - A check-in and a release are decided under the reservation's lock, and their time
      conditions are exact opposites, so a booking is never both checked in and released.
    - The single definition lives in `SmartDesk.Domain.Rules.CheckInRules`.
    - Anyone invited to the booking may check it in too (ADR-0032).
    - While a door screen is set up, only its code checks in: this 30-second step's or the
      last one's (ADR-0034). Five minutes before a release, the owner and those invited are
      warned.
11. **Meetings (ADR-0032).** A Manager invites their own team's active members, an Admin any
    active account, an Employee nobody. Each person added must be able to use the resource, and
    the resource's `Capacity` must hold everyone, the organizer included. A new start or another
    resource puts every answer back to Pending. A reason for not coming is only ever in the
    organizer's message.
12. **Series (ADR-0035).** Every date of a series follows the rules of any booking, and they're
    booked together, in one transaction under the resource's lock: one date that isn't free
    stops them all unless the booker chose to skip such dates. The dates keep the first one's
    local time in the booker's time zone; the last starts at most 180 days ahead; a weekly
    series includes the day it starts on; no date runs into the next.

## Concurrency (formerly an open question)

Settled in `docs/DECISIONS.md`, ADR-0011: an exclusion constraint *and* a per-resource lock -
see rule 5 above and the Constraints section.
