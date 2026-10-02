# SmartDesk API Contracts

Planned HTTP surface, written ahead of implementation so frontend work can start against a
stable contract. Update this file in the same PR as any endpoint you add, rename, or
change the shape of - it is the source of truth for what the API looks like, not the
controller code.

All endpoints are under `/api`. Authenticated endpoints expect `Authorization: Bearer
<jwt>`. Error responses use `application/problem+json` (RFC 7807 `ProblemDetails`). Messages
are in English or Turkish, as `Accept-Language` asks (see Languages under Conventions); the
examples below are the English ones.

## Auth

| Method | Path | Auth | Command/Query | Notes |
|---|---|---|---|---|
| POST | `/api/auth/login` | none | `LoginCommand` | body: `{ email, password }` → `{ accessToken, expiresAt, passwordChangeRequired }`, and a new **session** in the `smartdesk_session` cookie (see Sessions below). 400 if a field is missing/malformed, 401 on wrong credentials. With the right password, 403 for an account that isn't active yet or any more (see below). `passwordChangeRequired: true` means the password was set by someone else and must be changed first (ADR-0021). |
| POST | `/api/auth/refresh` | none (the session cookie) | `RefreshSessionCommand` | no body → the same answer as login, for the session in the cookie, whose refresh token is replaced. 204 when there's no cookie at all: nothing to renew, and no failed request for a guest's browser to show. 401 `"Your session has ended. Sign in again."`, and the cookie deleted, when its session ended, expired, or was used from a copy (see Sessions). Not rate limited. |
| POST | `/api/auth/logout` | none (the session cookie) | `LogoutCommand` | no body → 204. Ends the cookie's session on the server, so its access tokens stop working too, and deletes the cookie. Fine without a session, and fine twice. |
| POST | `/api/auth/register` | none | `RegisterCommand` | body: `{ firstName, lastName, email, password, managerEmail? }` → **202** for valid input, whether or not the email already has an account. Without `managerEmail`: a **Manager** who waits for an Admin's approval (ADR-0020). With it: an **Employee** asking to join that manager's team, who waits for that manager's approval (ADR-0021). 400 for missing/malformed fields (`managerEmail` must be a valid address and not the applicant's own), or with `Password`: Identity's reasons for refusing it. |
| GET | `/api/auth/me` | any authenticated user | `GetCurrentUserQuery` | → `{ id, firstName, lastName, email, roles: string[] }`. Also answers while a password change is required. |
| PUT | `/api/auth/me` | any authenticated user | `UpdateProfileCommand` | body: `{ firstName, lastName }` → 200 with the profile, as `GET` answers it. The user's own name: trimmed, required, at most 100 characters, no line breaks (the sign-up rules). Their sessions go on, and tokens issued from then on carry the new name; the app renews its session right away. The email and the role are changed by an Admin. 403 while a password change is required. |
| POST | `/api/auth/change-password` | any authenticated user | `ChangePasswordCommand` | body: `{ currentPassword, newPassword }` → 200 `{ accessToken, expiresAt, passwordChangeRequired: false }` - a **new token and a new session cookie**: every session of the account ends, this one included, and a new one starts for this browser (ADR-0021, ADR-0026). 400 with `CurrentPassword`: "The current password is wrong.", or with `NewPassword`: Identity's reasons, or "Choose a password different from the current one.". Answers while a password change is required. Shares the sign-in rate limit. |
| POST | `/api/auth/forgot-password` | none | `ForgotPasswordCommand` | body: `{ email }` → **202** always, whether or not an account has this email. For one that does, a reset link is emailed. 400 for a malformed email. |
| POST | `/api/auth/reset-password` | none | `ResetPasswordCommand` | body: `{ email, token, newPassword }` (email and token come from the emailed link) → 204. 400 with `Token`: "This reset link is invalid or has expired. Ask for a new one." (wrong, used, expired, or another email's token), or with `NewPassword`: Identity's reasons for refusing the password. |

- Login failures all return the same 401 `"Invalid email or password."` - unknown email,
  wrong password and locked-out account are deliberately indistinguishable. Five failed
  attempts lock the account for five minutes (ASP.NET Core Identity defaults).
- On top of that, one client (IP) can make at most `RateLimiting:LoginPermitsPerMinute`
  login attempts per minute - 10 by default, 60 in Development. Beyond that the API answers
  **429** with a `Retry-After` header (seconds) and a ProblemDetails body: `"Too many
  sign-in attempts. Please wait a minute and try again."`. The lockout protects one
  account; this limit protects against one client trying many accounts.
- **Sessions (ADR-0026).** Signing in starts a session that outlives the short access
  tokens issued under it:
  - The access token lasts `Jwt:AccessTokenLifetimeMinutes` (15 by default). The app keeps it
    in memory only, and renews it with `POST /api/auth/refresh` shortly before it expires.
  - The session is identified by a refresh token that only ever travels in the
    `smartdesk_session` cookie: `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/api/auth`. No
    script can read it, and it goes nowhere but the renew and sign-out calls. Responses that
    carry tokens are `Cache-Control: no-store`.
  - Each renewal replaces the refresh token. The one it replaced still works for
    `Sessions:ReuseGraceSeconds` (30), so two tabs renewing at once both get through. Used
    after that, it can only be a copy, and the whole session ends.
  - The replacement is atomic: of two renewals with the same token at the same moment, one
    replaces it, and the other gets an access token with no `Set-Cookie`, since the first
    answer brings the new token to the same browser.
  - The app keeps the user signed in when a renewal gets no answer (status 0, 5xx), and tries
    again; only an answer from the API (401) ends the session there.
  - A session ends after `Sessions:IdleDays` (14) without a renewal, after `Sessions:MaxDays`
    (30) in any case, on sign-out, and when the password changes or the account is switched
    off.
  - Every access token names its session (`sess`), and the API refuses a token whose
    session has ended, so signing out takes effect on the server at once, not when the token
    expires.
  - The server keeps only a SHA-256 hash of the refresh token.
- Token claims: `sub` (user id), `email`, `given_name`, `family_name`, one `role` claim per
  role (`Admin` / `Manager` / `Employee`), `sst` - a digest of the account's security
  stamp when the token was issued (ADR-0021) - and `sess`, the session it was issued under
  (ADR-0026). The user's team and a pending password change
  are never read from the token; the server looks them up on each request.
- **Account status (ADR-0020).** Signing in with the right password to an account that can't
  be used answers 403 with a message to show as-is: `"Your account is waiting for approval.
  We'll email you when it's ready."` or `"Your account has been disabled. Contact your
  administrator."`. A wrong password still gets the plain 401, so these
  say nothing to someone guessing emails.
- **A token only works while its account does.** On every request the token's user must
  still be Active, hold exactly the roles the token lists, and have the security stamp the
  token was issued under. Once an account is disabled, its role changes or its password changes
  (by the owner, a reset link, or a temporary password), its existing tokens get **401** on the
  next request (the answer is cached for up to 30 seconds, and dropped at once when SmartDesk
  itself makes the change), and its open real-time connections are cut. The frontend sends the
  user back to sign in.
- **Temporary passwords (ADR-0021).** An account created by an Admin or a Manager, or given a
  temporary password by one, must choose its own password first. Until it does, every endpoint
  except `GET /api/auth/me` and `POST /api/auth/change-password` answers **403** with
  `"code": "password-change-required"` in the ProblemDetails. The temporary password is never
  emailed; the owner is only told that it was set.
- **Sign-up (ADR-0020, ADR-0021).** Without `managerEmail`, a new email becomes a Manager
  account with status `PendingApproval`; the applicant gets a "we've received your sign-up"
  email and every active Admin gets one linking to `{App:FrontendUrl}/admin/users`. With
  `managerEmail`, it becomes a pending Employee: if the address is an active Manager's, the
  request joins their team and that manager is emailed a link to `{App:FrontendUrl}/team`;
  otherwise (unknown, not a manager, disabled or still waiting) it has no team and every active
  Admin is emailed, with the address the applicant typed. The applicant's email is the same
  either way, so the form can't be used to find out who the managers are. An email that already has
  an account changes nothing: its owner gets a "you already have an account" email instead,
  and the answer is the same 202. Password rules are checked before the email is looked up,
  so a weak password is refused the same way in both cases. Shares the sign-in rate limit.
- **Password reset (ADR-0019).** The emailed link is
  `{App:FrontendUrl}/reset-password?email=…&token=…`, where the token is URL-safe. It works for
  2 hours, and only once: a reset changes the account's security stamp, which the token is tied
  to. A successful reset also ends a lockout. `forgot-password` answers in the same time
  whether or not the account exists, because the email is queued rather than sent inline.
  Both endpoints share the sign-in rate limit above.

## Users (Admin)

Every account, for Admins (ADR-0020). An account is returned as:

```json
{
  "id": "…", "firstName": "Mehmet", "lastName": "Demir", "displayName": "Mehmet Demir",
  "email": "employee2@smartdesk.local", "role": "Employee", "status": "Active",
  "managerId": "…", "managerName": "Zeynep Kaya", "createdAt": "2026-09-28T07:20:00Z", "teamSize": 0,
  "mustChangePassword": false, "language": "tr"
}
```

`role` is one of `Admin`, `Manager`, `Employee`; `status` one of `Active`, `PendingApproval`,
`Disabled`. `managerId`/`managerName` are set only for an Employee in a team; `teamSize` is
the number of Employees whose manager this account is, requests to join included;
`mustChangePassword` is true until the owner replaces a temporary password. `language` (`"en"`,
`"tr"` or null) is what the account's emails are written in: the language it last signed in
with (ADR-0023).

| Method | Path | Auth | Command/Query | Notes |
|---|---|---|---|---|
| GET | `/api/users?status=&role=&search=` | Admin | `GetAccountsQuery` | → `[account]`. Sign-ups waiting for approval first, then by name. `search` matches name or email, case-insensitive. Unknown `role` → 400. |
| POST | `/api/users` | Admin | `CreateAccountCommand` | body: `{ firstName, lastName, email, role, password, managerId? }` → 201 with the account. Active, with `password` as a temporary password the Admin hands over; the owner must change it at first sign-in, and is emailed that the account exists (without the password). 400 with `Password`: Identity's reasons. `managerId` only for an Employee, and must be an active Manager (400 otherwise). Email already used → 409. |
| POST | `/api/users/{id}/approve` | Admin | `ApproveAccountCommand` | optional body `{ managerId }` puts an Employee into that team on the way (400 if not an active Manager; 409 for a Manager sign-up) → 200 with the account, now Active; the owner is emailed. 409 unless it's waiting for approval. |
| POST | `/api/users/{id}/reject` | Admin | `RejectAccountCommand` | → 204. Deletes the waiting sign-up (so the email can sign up again) and emails the applicant. 409 unless it's waiting for approval - a used account is disabled instead. |
| PATCH | `/api/users/{id}/role` | Admin | `ChangeAccountRoleCommand` | body: `{ "role": "Manager" }` → 200 with the account. Becoming a Manager or Admin takes an Employee out of their team. 409 for: your own account; a sign-up waiting for approval; a Manager who still has team members (`"Zeynep Kaya still has 2 team member(s). Move them to another manager or remove them from the team first."`); the last active Admin. Same role again is a no-op. |
| PATCH | `/api/users/{id}/manager` | Admin | `AssignManagerCommand` | body: `{ "managerId": "…" }` or `{ "managerId": null }` → 200 with the account. Moves an Employee into that Manager's team, or out of any team. 409 if the account isn't an Employee; 400 (`managerId`) if the manager isn't an active Manager. |
| POST | `/api/users/{id}/disable` | Admin | `SetAccountStatusCommand` | → 200 with the account, now Disabled: it can't sign in, and its tokens stop working. 409 for your own account, a sign-up waiting for approval (approve or reject it instead), or the last active Admin. Already disabled is a no-op. Reservations are kept. |
| POST | `/api/users/{id}/enable` | Admin | `SetAccountStatusCommand` | → 200 with the account, Active again. |
| POST | `/api/users/{id}/password` | Admin | `SetTemporaryPasswordCommand` | body: `{ password }` → 200 with the account. A temporary password the owner must replace at their next sign-in; all their sessions end, a lockout is lifted, and they're emailed that it happened (without the password). 400 with `Password`: Identity's reasons (nothing changes then). 409 for your own account (change it under your account instead) or a sign-up waiting for approval. |

All of them: unknown `id` → 404, non-Admin → 403.

## Team (Manager)

A Manager's own team (ADR-0020). An account outside the caller's team - another team's member,
another Manager, an Admin - answers **404**, the same as an id that doesn't exist.

| Method | Path | Auth | Command/Query | Notes |
|---|---|---|---|---|
| GET | `/api/team` | Manager | `GetMyTeamQuery` | → `[account]` (shape above): requests to join the team (`PendingApproval`) first, then the members by name |
| POST | `/api/team/members` | Manager | `AddTeamMemberCommand` | body: `{ firstName, lastName, email, password }` → 201 with the new account: an Active Employee in the caller's team, with a temporary password (as for `POST /api/users`). Email already used → 409. |
| POST | `/api/team/requests/{id}/approve` | Manager | `ApproveAccountCommand` | → 200 with the account: a request to join the caller's team, now an Active member; the applicant is emailed. 409 unless it's waiting. |
| POST | `/api/team/requests/{id}/reject` | Manager | `RejectAccountCommand` | → 204. Deletes the request and emails the applicant. |
| DELETE | `/api/team/members/{id}` | Manager | `RemoveTeamMemberCommand` | → 204. Takes them out of the team; the account stays, without a team, until an Admin moves or disables it. 409 for a request to join (approve or reject it instead). |
| POST | `/api/team/members/{id}/disable` | Manager | `SetAccountStatusCommand` | → 200 with the account; as for `/api/users/{id}/disable` |
| POST | `/api/team/members/{id}/enable` | Manager | `SetAccountStatusCommand` | → 200 with the account |
| POST | `/api/team/members/{id}/password` | Manager | `SetTemporaryPasswordCommand` | body: `{ password }` → 200 with the account; as for `/api/users/{id}/password` |

## Resource types

| Method | Path | Auth | Command/Query | Notes |
|---|---|---|---|---|
| GET | `/api/resource-types` | any authenticated user | `GetResourceTypesQuery` | → `[{ id, name }]`, sorted by name, for populating forms/filters |

Types are reference data seeded by migration: Meeting Room, Desk, Company Car, Projector,
Camera (fixed ids, same in every environment). There's no endpoint to add types yet.

## Resources

**Teams (ADR-0022).** A resource belongs to one or more managers' teams, or to none, which
makes it shared by everyone. A team's members and its manager may see and book its resources;
nobody else may, apart from Admins, who see and manage everything. For anyone else, another
team's resource behaves as if it didn't exist: it's left out of every list and search, and asking
for it by id, booking it or blocking it answers **404**. A resource several teams share is booked
first come, first served, under the usual overlap rule.

| Method | Path | Auth | Command/Query | Notes |
|---|---|---|---|---|
| GET | `/api/resources` | any authenticated user | `GetResourcesQuery` | optional query params `resourceTypeId`, `status`. Every status unless filtered (the Resources screen shows inactive ones greyed out), but only resources the caller may use. |
| GET | `/api/resources/{id}` | any authenticated user | `GetResourceByIdQuery` | 404 if it doesn't exist or belongs to other teams. For the edit form. |
| GET | `/api/resources/available` | any authenticated user | `GetAvailableResourcesQuery` | query params: `startDateTime`, `endDateTime`, optional `resourceTypeId`, `minCapacity`. Rules below. |
| POST | `/api/resources` | Admin or Manager | `CreateResourceCommand` | body: `{ name, resourceTypeId, capacity, location, description?, features?: string[], teamIds?: guid[], checkInRequired?: bool }` → 201 with `Location` header. Always created `Active`. An Admin chooses `teamIds` (none: shared; each must be an active Manager's, 400 otherwise). A Manager's resource always belongs to their own team only; naming another team is 403. `checkInRequired`: see Check-in below. |
| PUT | `/api/resources/{id}` | Admin or Manager | `UpdateResourceCommand` | body: the POST fields (without `teamIds`) plus `status`. Full replace: omitting `features` removes them all, and omitting `checkInRequired` switches check-in off. A Manager may edit only a resource that belongs to their team alone; a shared resource, or one other teams share, is 403. Teams don't change here. A `status` other than `Active` follows the PATCH rule below (409 while bookings are still to come). |
| PATCH | `/api/resources/{id}/status` | Admin or Manager | `ChangeResourceStatusCommand` | body: `{ "status": "Inactive" }` → 200 with the resource. Changes only the status, so an Activate/Deactivate button can't overwrite another admin's edit the way a full PUT can. Same status again is a no-op. `Maintenance` or `Inactive` is refused with 409 (`code: "resource-has-reservations"`, `"2 confirmed reservation(s) on Camera C-1 from now on. Cancel or move them first."`) while `Confirmed` reservations on it are still to start; past and running ones don't count, and going back to `Active` is always allowed (issue #41, ADR-0024). A Manager, as for PUT. |
| PATCH | `/api/resources/{id}/teams` | Admin | `SetResourceTeamsCommand` | body: `{ "teamIds": ["…"] }` → 200 with the resource. Replaces its teams; `[]` makes it shared. Each id must be an active Manager's (400 otherwise), at most 20. Existing reservations are kept. |
| GET | `/api/resources/{id}/photo` | any authenticated user | `GetResourcePhotoQuery` | The photo's bytes, as `image/jpeg`, `image/png` or `image/webp`. 404 when it has none, or for another team's resource. `Cache-Control: private, no-cache` with an `ETag`: send `If-None-Match` and an unchanged photo is a 304. The app adds `?v={photoUpdatedAt}`, which the API ignores, so a browser never offers an older photo (ADR-0027). |
| PUT | `/api/resources/{id}/photo` | Admin or Manager | `SetResourcePhotoCommand` | `multipart/form-data` with the file in the field `photo` → 200 with the resource. Sets or replaces it. A JPEG, PNG or WebP of at most 2 MB, recognised by its first bytes, whatever name or type the upload gives (400 on `Photo` otherwise; no SVG). The request itself may be up to 8 MB, so a photo a little over the limit still gets that message. Who may: as for PUT. |
| DELETE | `/api/resources/{id}/photo` | Admin or Manager | `RemoveResourcePhotoCommand` | 204. Its type's picture shows instead; removing a photo that isn't there is fine. Who may: as for PUT. |
| POST | `/api/resources/{id}/check-in` | any authenticated user | `CheckInCommand` | body: `{ "code": "…" }`, the secret from the resource's QR code → 200 with the reservation, `checkedInAt` set. Checks in the caller's own booking of this resource whose check-in is open now; checking in twice is fine. Errors under Check-in below. |
| GET | `/api/resources/{id}/check-in-code` | Admin or Manager | `GetCheckInCodeQuery` | → 200 `{ "code": "…", "url": "https://…/check-in/{id}?code=…" }`: the QR code to print. `url` is built from `App:FrontendUrl`, like the links in emails. Every resource has a code, check-in on or off. Who may: as for PUT (403 otherwise). The secret appears nowhere else. |
| POST | `/api/resources/{id}/check-in-code` | Admin or Manager | `ReplaceCheckInCodeCommand` | → 200 with a new `{ code, url }`. The printed code stops working at once. Who may: as for PUT. |

Every resource endpoint returns the same shape:

```json
{
  "id": "…", "name": "Meeting Room A",
  "resourceTypeId": "…", "resourceTypeName": "Meeting Room",
  "capacity": 8, "location": "2nd Floor",
  "status": "Active",            // "Active" | "Inactive" | "Maintenance"
  "description": null,
  "features": ["TV", "Video Conference"],
  "teams": [{ "id": "…", "managerName": "Zeynep Kaya" }],  // [] = shared by everyone
  "photoUpdatedAt": "2026-10-05T08:00:00.123456Z",         // null = no photo of its own
  "checkInRequired": false                                 // bookings must be checked in at the door
}
```

`photoUpdatedAt` says when the photo last changed, never the photo itself, so lists stay small;
the photo comes from `GET /api/resources/{id}/photo`.

**Validation (400):** `name` and `location` required, max 200 chars; `description` max 1000;
`capacity` 1-1000; `resourceTypeId` must exist; at most 20 `features`, each 1-100 chars, no
duplicates ignoring case and surrounding spaces (`"TV"` and `" tv"` clash).

**Availability rules** (`/api/resources/available`):
- Only `Active` resources the caller may use are returned; `Inactive` / `Maintenance` never are.
- A resource with a block (see Resource blocks) overlapping the window isn't returned either.
- A resource is excluded if it has a `Confirmed` reservation overlapping
  `[startDateTime, endDateTime)`. Ranges are half-open, so back-to-back bookings are fine:
  a reservation ending at 11:00 does not block a search starting at 11:00. Cancelled
  reservations never block.
- `endDateTime` must be after `startDateTime`; the window can be at most 31 days.
- `startDateTime` can be at most 180 days ahead, as for a booking (400 otherwise).
  `minCapacity`, if given, must be positive. Past windows aren't rejected here (it's a
  read-only search); creating a reservation in the past is rejected by
  `CreateReservationCommand`.
- The result is a snapshot. A slot shown as free can be taken a second later, so
  `POST /api/reservations` re-checks and can still answer 409.
- Send times as UTC (`2026-10-05T11:00:00Z`). An explicit offset (`+03:00`) is converted
  correctly, and a time without an offset is treated as UTC.

## Reservations

| Method | Path | Auth | Command/Query | Notes |
|---|---|---|---|---|
| POST | `/api/reservations` | any authenticated user | `CreateReservationCommand` | body: `{ resourceId, startDateTime, endDateTime, note?, reminderMinutes?, attendeeIds? }` → 201. The owner is always the caller. `reminderMinutes`: 5, 15, 30 or 60, or null / omitted for no reminder (see Real-time). `attendeeIds`: who's invited (see Meetings). Another team's resource is 404 (ADR-0022). |
| GET | `/api/reservations/{id}` | any authenticated user | `GetReservationByIdQuery` | one reservation, any status; 404 if it doesn't exist. For the edit form and for opening a calendar entry. Your own reservation always; anyone else's only on a resource you may use (404 otherwise, ADR-0022). |
| PUT | `/api/reservations/{id}` | owner or Admin | `UpdateReservationCommand` | body: `{ resourceId, startDateTime, endDateTime, note?, reminderMinutes?, attendeeIds? }` - full replace → 200, so leaving `reminderMinutes` out turns the reminder off, and leaving `attendeeIds` out takes everyone off the list. Rules below. |
| POST | `/api/reservations/{id}/cancel` | owner or Admin | `CancelReservationCommand` | → 200 with the cancelled reservation. Idempotent: cancelling an already-cancelled reservation is a no-op, not an error, and sends no second event. |
| GET | `/api/reservations/mine` | any authenticated user | `GetMyReservationsQuery` | the caller's reservations - upcoming, past and cancelled - oldest start first |
| GET | `/api/reservations/invited` | any authenticated user | `GetMyInvitationsQuery` | other people's `Confirmed` meetings the caller is invited to and that aren't over, soonest first (ADR-0032) |
| GET | `/api/reservations/invitees` | any authenticated user | `GetInviteesQuery` | who the caller may invite: `[{ id, name, email }]`, by name - a Manager's active team members, every active account for an Admin, nobody for an Employee |
| POST | `/api/reservations/{id}/response` | someone invited | `RespondToInvitationCommand` | body: `{ response: "Accepted" \| "Declined", reason? }` → 200 with the meeting. Declining needs a `reason` (max 500), which goes to the organizer only. See Meetings. |
| GET | `/api/reservations?from=&to=&resourceId=&resourceTypeId=` | any authenticated user | `GetReservationsQuery` | the calendar: `Confirmed` reservations overlapping `[from, to)`, optionally for one resource or type, earliest first. `to` must be after `from`, at most 42 days apart - a six-week month grid - plus an hour, because six local weeks that cross the autumn clock change are 42 days and an hour (400 otherwise). Everyone sees who booked what - it's a shared office calendar. Only reservations on resources the caller may use (ADR-0022). |

Every reservation endpoint returns this shape; it is also the payload of the SignalR events:

```json
{
  "id": "…", "resourceId": "…",
  "resourceName": "Meeting Room A", "resourceTypeId": "…", "resourceTypeName": "Meeting Room",
  "resourceLocation": "2nd Floor",
  "userId": "…",
  "startDateTime": "2026-10-05T10:00:00Z", "endDateTime": "2026-10-05T11:30:00Z",
  "status": "Confirmed",          // "Confirmed" | "Cancelled"
  "note": "Sprint planning",
  "reminderMinutes": 15,           // null when no reminder is set
  "checkInRequired": true,         // has to be checked in at the door, or it's released
  "checkedInAt": null,             // when the owner checked in
  "releasedAt": null,              // when it was released because nobody did (status is then "Cancelled")
  "userDisplayName": "Ayşe Yılmaz", // who booked it: "First Last", or the email if both are blank
  "attendees": [                   // who's invited, by name (ADR-0032); usually empty
    { "id": "…", "name": "Mehmet Demir", "response": "Accepted" }  // "Pending" | "Accepted" | "Declined"
  ]
}
```

A reservation appears in a calendar range exactly when it would block a booking of that range
(same half-open overlap rule as availability and create).

**Create - errors**

| Status | When |
|---|---|
| 400 | `endDateTime` not after `startDateTime`; `startDateTime` more than 5 minutes in the past, or more than 180 days ahead (ADR-0024); longer than 31 days; `note` over 500 chars; `reminderMinutes` not one of 5, 15, 30, 60 |
| 404 | the resource doesn't exist |
| 409 | the resource isn't `Active` (`code: "resource-unavailable"`, `"Camera C-1 can't be reserved right now (status: Maintenance)."`), it has a block overlapping the range (`code: "resource-blocked"`, `"Meeting Room A is blocked for part of that time: Projector replacement"`), or it already has a `Confirmed` reservation overlapping `[startDateTime, endDateTime)` (`code: "reservation-overlaps"`, `"Meeting Room A is already reserved for part of that time."`) |

The 409 detail is written to be shown to the user as-is. Overlap uses the same half-open rule as
the availability search, so back-to-back bookings are allowed. It holds under concurrent
requests: of several simultaneous bookings for one slot, exactly one gets 201 and the rest 409
(ADR-0011).

**Cancel - errors:** 403 if the caller is neither the owner nor an Admin; 404 if the
reservation doesn't exist; 409 if it has already ended.

**Update - rules and errors.** The owner never changes, even when an Admin edits.

| State of the reservation | What can change |
|---|---|
| upcoming (not started) | anything - resource, start, end, note. A new start follows the create rules (not in the past, with the 5-minute grace, and at most 180 days ahead → 400 otherwise) |
| in progress (started, not ended) | only `endDateTime` (must stay in the future) and `note`; changing the start or the resource → 409 |
| ended, or cancelled | nothing → 409 |

When the resource or the times change, the new slot goes through the same checks as a new
booking: the resource must exist (404) and be `Active` (409), and it must not overlap another
`Confirmed` reservation (409). The reservation's own current slot doesn't count, so shifting
10:00-11:00 to 10:30-11:30 works. A change to the note alone skips those checks, and so does
shortening on the same resource (a new start and end within the current ones), which claims
nothing new. So after the resource went into maintenance, a note can still be edited and a
booking can still be shortened or ended early. Other errors: 400 for the
same shape rules as create, 403 for someone who is neither the owner nor an Admin, 404 for an
unknown reservation. A PUT that changes nothing returns 200 and sends no event.

## Meetings (ADR-0032)

A booking can invite people. The organizer is the booking's owner; everyone invited gets a
message in their inbox and answers it.

- **Who may invite.** A Manager their own team's active members, an Admin any active account,
  an Employee nobody (`GET /api/reservations/invitees` lists them). Each person added must be
  able to use the resource (ADR-0022), and the room's `capacity` must hold everyone, the
  organizer included. At most 50 people. The organizer is never on their own list, and nobody
  is on it twice.
- **Changing the list.** `attendeeIds` on create and update is the whole list. Whoever is added
  is invited; whoever is taken off is told; someone already on it stays even after leaving the
  team. A new time or room tells everyone still invited and asks them to answer again (a new
  start or another room; a longer meeting keeps the answers). Changing only the note tells
  nobody. Cancelling, or a release, tells everyone.
- **Answers.** `POST /api/reservations/{id}/response`, by someone on the list, while the meeting
  is `Confirmed` and not over. `Declined` needs a `reason`. The organizer gets a message with
  the answer, and the reason; every screen showing the meeting gets it as `ReservationUpdated`.
  The same answer again changes nothing and tells nobody. A reason is never part of a
  reservation: only the organizer's message has it.
- **Check-in.** Anyone invited may check the meeting in (ADR-0030): whoever is first keeps the
  room for everyone.

**Meetings - errors**

| Status | When |
|---|---|
| 400 | on create or update: an Employee adds someone (`"Only team managers and administrators can invite people."`), someone added isn't one the caller may invite, can't use the resource (`"Mehmet Demir can't use Board Room: it belongs to another team."`), the room is too small (`"Board Room has room for 6; with you, 8 people would come."`), more than 50 people. On an answer: `response` isn't `Accepted` or `Declined`, or declining without a `reason` (`"Say why you can't come."`). All under `AttendeeIds`, `Response` or `Reason` |
| 403 | answering a meeting the caller isn't invited to (`"You're not on this meeting's list."`) |
| 404 | the meeting doesn't exist |
| 409 | answering a meeting that was cancelled, or is over |

## Inbox (ADR-0032)

The signed-in user's own messages; nobody sees anyone else's.

| Method | Path | Auth | Command/Query | Notes |
|---|---|---|---|---|
| GET | `/api/inbox` | any authenticated user | `GetInboxQuery` | the newest 100 messages, read or not, newest first |
| GET | `/api/inbox/unread-count` | any authenticated user | `GetUnreadInboxCountQuery` | `{ unread }`, for the badge |
| POST | `/api/inbox/{id}/read` | the recipient | `MarkInboxMessageReadCommand` | → 204; again changes nothing; someone else's message is 404 |
| POST | `/api/inbox/read-all` | any authenticated user | `MarkAllInboxMessagesReadCommand` | → 204 |

```json
{
  "id": "…", "recipientId": "…",
  "kind": "MeetingInvitation",   // MeetingInvitation | MeetingChanged | MeetingCancelled | MeetingUninvited
                                 // | MeetingAccepted | MeetingDeclined (the last two to the organizer)
  "reservationId": "…",          // null once the booking is gone
  "senderId": "…", "senderName": "Zeynep Kaya",  // both null for a release, which nobody sent
  "resourceName": "Board Room",  // the meeting as it was when the message was sent
  "startDateTime": "2026-10-05T10:00:00Z", "endDateTime": "2026-10-05T11:00:00Z",
  "note": "Sprint planning",
  "reason": null,                // why someone can't come, in a MeetingDeclined
  "createdAt": "2026-10-01T09:00:00Z",
  "readAt": null                 // null while unread
}
```

The app writes the sentence from `kind`, in the reader's language.

## Check-in (ADR-0030)

A resource with `checkInRequired` has a QR code at its door. Whoever booked it scans the code,
which opens `/check-in/{id}?code=…` in the app, and checks in there
(`POST /api/resources/{id}/check-in`).

- **The window.** From 10 minutes before the start until 15 minutes after, and never once the
  booking is over. The owner checks in, or anyone invited to it (ADR-0032), and only a
  `Confirmed` booking.
- **Release.** A booking nobody checked in to by then is released: its `status` becomes
  `Cancelled` and `releasedAt` is set. A background job looks every 30 seconds
  (`CheckIn:IntervalSeconds`; `CheckIn:Enabled: false` switches releasing off), so a release
  can be up to one interval late, never early. A booking that's already over is left alone.
- **Who needs a check-in.** A booking needs one when its resource asked for it when it was booked
  (or last moved to a new start or another resource) and still does. That's the `checkInRequired`
  a reservation carries.
  - Staying longer, or ending early, keeps it: a meeting that runs over isn't released for a
    check-in nobody asked of it when it was booked.
  - Switching check-in on doesn't catch bookings made without knowing.
  - Switching it off stops every release at once.
- **Moving a booking.** A new start, or another resource, undoes the check-in: that's another
  visit. Ending early keeps it.
- **Several bookings.** When two of the caller's bookings are open at once (back to back), the
  one still waiting goes first.

**Check-in - errors**

| Status | When |
|---|---|
| 400 | no `code`, or longer than a code |
| 403 | the code isn't the resource's current one: printed before it was replaced (`"This QR code is no longer valid. Scan the current code for Board Room."`) |
| 404 | the resource doesn't exist, or belongs to other teams |
| 409 | the resource doesn't use check-in (`code: "check-in-not-used"`), or none of the caller's bookings of it is open for one now (`code: "nothing-to-check-in"`, `"You have no booking of Board Room to check in to right now. Check-in opens 10 minutes before the start and closes 15 minutes after it."`) |

## Resource blocks

A block takes a resource out of booking for one window - planned maintenance, an event, a
car at the service - without changing its status. Blocks use the same half-open
`[start, end)` rule as reservations.

| Method | Path | Auth | Command/Query | Notes |
|---|---|---|---|---|
| GET | `/api/resource-blocks?from=&to=&resourceId=` | any authenticated user | `GetResourceBlocksQuery` | blocks overlapping `[from, to)`, earliest first; same limit as the calendar (42 days plus an hour). Drawn next to the calendar's reservations. Only blocks on resources the caller may use. |
| POST | `/api/resource-blocks` | Admin or Manager | `CreateResourceBlockCommand` | body: `{ resourceId, startDateTime, endDateTime, reason }` → 201. A Manager may block only a resource that belongs to their team alone (403 for a shared one; 404 for another team's, ADR-0022). |
| DELETE | `/api/resource-blocks/{id}` | Admin or Manager | `DeleteResourceBlockCommand` | → 204; 404 if it doesn't exist. A Manager, as for POST. |

```json
{
  "id": "…", "resourceId": "…", "resourceName": "Meeting Room A",
  "startDateTime": "2026-10-05T10:00:00Z", "endDateTime": "2026-10-05T14:00:00Z",
  "reason": "Projector replacement",
  "createdByUserId": "…"
}
```

**Create - errors**

| Status | When |
|---|---|
| 400 | start more than 5 minutes in the past; end not after start; longer than 366 days; `reason` empty or over 200 chars |
| 404 | the resource doesn't exist |
| 409 | another block on the resource overlaps (`code: "block-overlaps-block"`, `"Meeting Room A is already blocked for part of that time."`), or `Confirmed` reservations overlap (`code: "block-overlaps-reservations"`, `"2 confirmed reservation(s) on Meeting Room A overlap this window. Cancel or move them first."`) |

Existing reservations are never cancelled by a block. The admin cancels or moves them first,
with the reservation endpoints, and then blocks. While a block exists, booking or moving a
reservation into its window returns 409 with the block's `reason`, and availability search
leaves the resource out.

## Dashboard

| Method | Path | Auth | Command/Query | Notes |
|---|---|---|---|---|
| GET | `/api/dashboard?timeZone=Europe/Istanbul` | any authenticated user | `GetDashboardQuery` | today's reservations, resources currently in use, the caller's upcoming reservations Today's list, in-use list and the active-resource count cover only resources the caller may use; their own upcoming reservations are all theirs (ADR-0022). |

`timeZone` is an IANA id - in the browser, `Intl.DateTimeFormat().resolvedOptions().timeZone`.
It decides what "today" means: the server runs on UTC, so without it 01:30 in Istanbul would
still count as yesterday. Omitted → UTC. Unknown id → 400.

```json
{
  "generatedAt": "2026-09-23T15:13:00Z",
  "timeZone": "Europe/Istanbul",
  "todayStart": "2026-09-22T21:00:00Z",   // local midnight, as UTC
  "todayEnd": "2026-09-23T21:00:00Z",     // next local midnight (23 or 25 h later on DST days)
  "stats": { "activeResources": 7, "resourcesInUseNow": 1, "reservationsToday": 2, "myUpcomingReservations": 1 },
  "today": [ /* every Confirmed reservation overlapping today, all resources, earliest first */ ],
  "inUseNow": [ /* Confirmed reservations running right now, ending soonest first */ ],
  "myUpcoming": [ /* the caller's next 5 Confirmed reservations that haven't started */ ]
}
```

Every list holds the reservation shape shown under Reservations. `stats.myUpcomingReservations`
is the full count, while `myUpcoming` is capped at 5.

## Reports

| Method | Path | Auth | Command/Query | Notes |
|---|---|---|---|---|
| GET | `/api/reports/usage?from=2026-09-01&to=2026-09-30&timeZone=Europe/Istanbul&resourceTypeId=` | Admin | `GetUsageReportQuery` | how resources were used over whole local days, `from` and `to` both included (at most 92 days). `timeZone` works as on the dashboard (omitted → UTC, unknown → 400). `to` before `from` → 400. |

```json
{
  "from": "2026-09-01", "to": "2026-09-30", "timeZone": "Europe/Istanbul",
  "fromUtc": "2026-08-31T21:00:00Z", "toUtc": "2026-09-30T21:00:00Z",
  "workingHours": "Mon-Fri 09:00-18:00",
  "workingHoursPerResource": 198,        // office hours in the range, per resource
  "totals": { "resources": 7, "reservations": 42, "cancelled": 5, "bookedHours": 120.5,
              "workingHoursBooked": 98, "workingHoursAvailable": 1386, "utilization": 0.0707 },
  "resources": [                           // busiest first (utilization, then booked hours)
    { "resourceId": "…", "resourceName": "Meeting Room A", "resourceTypeId": "…",
      "resourceTypeName": "Meeting Room", "status": "Active", "reservations": 12, "cancelled": 1,
      "bookedHours": 30.5, "workingHoursBooked": 28, "utilization": 0.1414 }
  ],
  "byType": [ { "resourceTypeId": "…", "resourceTypeName": "Meeting Room", "resources": 3, "reservations": 20,
                "cancelled": 2, "bookedHours": 55, "workingHoursBooked": 50, "workingHoursAvailable": 594, "utilization": 0.0842 } ],
  "topUsers": [ { "userId": "…", "displayName": "Ayşe Yılmaz", "reservations": 9, "bookedHours": 21.5 } ]  // at most 5
}
```

What the numbers mean (ADR-0017):
- **`reservations` / `cancelled`**: `Confirmed` / `Cancelled` reservations whose time overlaps the
  range. One that spans the edge counts once.
- **`bookedHours`**: the part of each `Confirmed` reservation inside the range, whenever it falls,
  nights and weekends included.
- **`workingHoursBooked`**: the same, counting only Monday to Friday, 09:00-18:00 local time.
- **`utilization`**: `workingHoursBooked` divided by the office hours available, from 0 to 1. Per
  resource, that's `workingHoursPerResource`. For a type or the totals, it's that times the number
  of resources. A range with no weekday gives 0.
- **Which resources appear**: every `Active` resource, plus any other that had reservations in the
  range. Blocked windows are not subtracted from the available hours.

## Assistant

Optional (ADR-0029): answers only when the server has `Assistant:ApiKey`.

| Method | Path | Auth | Command/Query | Notes |
|---|---|---|---|---|
| GET | `/api/assistant` | any authenticated user | `GetAssistantStatusQuery` | → 200 `{ enabled }`: whether this server has an assistant. The app shows it only then. |
| POST | `/api/assistant/messages` | any authenticated user | `AskAssistantCommand` | body: `{ messages: [{ role, text }], timeZone? }` → 200 `{ reply, proposals }`. 400, 429 per user, 503 - see below. |

- **`messages`** is the whole conversation so far, oldest first: `role` is `"user"` or
  `"assistant"`, and the last one is the user's new question. At most 40 messages of up to
  2,000 characters each. The server keeps nothing between requests.
- **`timeZone`**: an IANA id, the browser's. "Tomorrow at 10" is worked out in it; omitted → UTC,
  unknown → 400.
- **`reply`** is plain text. Lines starting with `- ` are a list, and a blank line ends a
  paragraph.
- **`proposals`**: up to 3 cards, and nothing has happened yet. Pressing a card's button is the
  app's own request:

  ```json
  { "kind": "book", "resourceId": "…", "reservationId": null, "resourceName": "Meeting Room A",
    "startDateTime": "2026-10-01T11:00:00Z", "endDateTime": "2026-10-01T12:00:00Z", "note": "Sprint planning" }
  ```

  - `kind: "book"`: send `POST /api/reservations` with `resourceId`, the times and the note.
    The proposal checked that the resource was free, but the booking is what decides: it can
    still get a 409.
  - `kind: "cancel"`: send `POST /api/reservations/{reservationId}/cancel`. The rest describes
    the reservation.
- **Errors**:
  - 400 when there are no messages, more than 40, the last isn't the user's, one is empty or
    longer than 2,000 characters, a role is unknown, or the time zone is unknown.
  - 429 after `RateLimiting:AssistantPermitsPerMinute` questions in a minute from one user
    (20 by default), with `Retry-After` and "You've asked the assistant a lot in the last
    minute. Wait a moment and try again.".
  - 503 "The assistant isn't set up on this server." without a key, and "The assistant can't
    answer right now. Try again in a moment." when the model can't be reached, refuses the
    key, or the answer takes longer than `Assistant:TimeoutSeconds` (90 by default).

## Real-time (SignalR)

| Hub | Path | Events |
|---|---|---|
| `ReservationHub` | `/hubs/reservations` | `ReservationCreated`, `ReservationUpdated`, `ReservationCancelled` - each carries enough of the reservation's shape for an open calendar/dashboard to patch its view without a refetch. `ReservationStartingSoon` - the owner's reminder, sent to that user only. `ReservationReleased` - also to the owner only: their booking was released because nobody checked in (ADR-0030). `InboxMessageReceived` - a message to the user's inbox, to that user only, carrying the message (ADR-0032). Events on a shared resource go to everyone; on a team resource only to that resource's teams, the Admins and the reservation's owner (ADR-0022) - the owner may get such an event twice, so apply events by id. |

All three events carry the reservation object shown under Reservations above, as it is after
the change. For `ReservationUpdated` the resource or the times may be different from
before, so screens should replace the entry by `id` rather than patch it in place. Every
event includes
`userDisplayName`, so a screen can say who just booked or cancelled. Handlers raise them
through `IReservationNotifier` only after the change is committed.
`SignalRReservationNotifier` sends each event to every connected client, and does not
use the HTTP request's cancellation token: a closed tab must not swallow a change that
already committed. Enum values in the payload are names (`"Confirmed"`), same as the
HTTP API. A client that missed events while disconnected catches up by refetching when
the connection comes back; the broadcast is not a queue.

**Reminders (ADR-0018).** `ReservationStartingSoon` carries the same reservation object and goes
only to the owner's open connections. A background job checks every 30 seconds
(`Reminders:IntervalSeconds`) and sends it once, as soon as `startDateTime - reminderMinutes`
has passed, so it can be up to one interval late, never early. It isn't sent for a cancelled
reservation, or once the reservation has started. Moving the start or changing `reminderMinutes`
re-arms it for the new moment, while changing only the end or the note doesn't. A reminder that
finds the owner offline isn't queued or repeated.

**Inbox (ADR-0032).** `InboxMessageReceived` carries the message as `GET /api/inbox` returns it,
sent once its change is committed, to its recipient's connections only. One that finds them
offline isn't repeated: the inbox and `unread-count` have it.

**Check-in (ADR-0030).** A check-in is a `ReservationUpdated` with `checkedInAt` set. A release
reaches everyone who may see the resource as `ReservationCancelled`, with `releasedAt` set, so
calendars free the slot; the owner also gets `ReservationReleased` with the same reservation,
to be told why it's gone.

## Conventions

- Timestamps are UTC ISO-8601 over the wire; the frontend converts to local time for
  display. A timestamp sent without an offset is treated as UTC.
- Enums are serialized as their names (`"Active"`, `"Maintenance"`), not numbers, both in
  responses and in request bodies/query strings.
- A body that can't be parsed (a malformed date, an unknown enum name) gets a 400 whose
  `errors` key is the JSON path (`"$.status"`) with the generic message `"The input was not
  valid."` - the serializer's own message isn't passed through.
- List endpoints that can grow (reservations, resources) should be paginated once real
  data volume shows up - not needed for the MVP's expected data size.
- DTOs are never the EF entities directly (see `docs/DECISIONS.md`, ADR-0002).
- Every endpoint requires an authenticated user unless it's explicitly marked anonymous
  (ADR-0008) - "none" in an Auth column above means `[AllowAnonymous]`.
- Error status codes: 400 validation (`errors` holds a field → messages map), 401 missing/
  invalid token or failed login, 403 authenticated but not allowed (e.g. non-Admin), 409
  business-rule conflict (e.g. overlapping reservation), 429 too many login attempts or
  questions to the assistant, 500 unexpected, 503 the assistant is off or can't answer.
- **Languages (ADR-0023).** Every message a person reads - `title`, `detail`, validation
  messages, Identity's password rules - is in the language `Accept-Language` asks for:
  Turkish (`tr`, `tr-TR`, …) or English. No header, or any other language, gets English, so
  tools and scripts see the same messages as always. The frontend sends the app's own
  language, not the browser's.
  - Field keys in `errors` (`"EndDateTime"`) and enum values never change with the language;
    only the text does.
  - A 409 a screen may need to act on also carries a stable `code`, since its wording depends
    on the language: `resource-unavailable`, `resource-blocked`, `reservation-overlaps`,
    `resource-has-reservations`,
    `block-overlaps-block`, `block-overlaps-reservations`, `check-in-not-used`,
    `nothing-to-check-in`. The 403 for a pending password
    change has `code: "password-change-required"` (ADR-0021).
  - Emails are written in the recipient's language: the one they last signed in with, saved
    on their account. Until then, the language of the request that sends the email (for a new
    account, whoever created it; for a sign-up, the applicant's).
  - Messages that come from ASP.NET Core itself rather than SmartDesk - an unparseable body's
    `"The input was not valid."`, an empty 401 from the token check - stay in English.

## Health

| Method | Path | Auth | Notes |
|---|---|---|---|
| GET | `/health` | none | `200 Healthy` when the API can reach PostgreSQL and the database has no pending migrations, `503 Unhealthy` otherwise. Plain text, for container health checks and uptime monitors. |
