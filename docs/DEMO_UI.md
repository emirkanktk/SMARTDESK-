# SmartDesk UI Demo

A 15-minute walkthrough of the Angular app, the companion to [DEMO.md](DEMO.md) (the backend
through Swagger UI). It follows the same scenario, the brief's conflict example, but in the
browser, with two windows side by side so the audience sees changes arrive live.

## Before the demo

1. Start PostgreSQL and the API as in DEMO.md, steps 1 and 3 (clean slate), but with the
   `https` launch profile, which serves `https://localhost:7065` - the address the Angular dev
   server calls:
   ```bash
   dotnet run --project src/SmartDesk.API --launch-profile https
   ```
   A bare `dotnet run` picks the `http` profile, which listens on `http://localhost:5291` only,
   and every list in the app stays empty. The browser must trust the dev certificate
   (`dotnet dev-certs https --trust`, see README).
2. Start the frontend:
   ```bash
   cd frontend && npm install && ng serve
   ```
3. Open two windows side by side at `http://localhost:4200`: a normal one for **Ayşe**
   (`employee1@smartdesk.local`) and a private one for **Mehmet** (`employee2@smartdesk.local`).
   All passwords are `SmartDesk123!`. Both open on the front page. In Mehmet's window, **Sign
   in** and open **Calendar**.
4. Pick a working day in the current week as `DAY` (or next week, then press **›** in the
   Calendar). Times below are local; 13:00 in Istanbul is DEMO.md's 10:00Z.

## Walkthrough

### 1. Front page, sign in and the dashboard

- Ayşe's window shows the public front page: what SmartDesk does, how booking works, and, in a
  development build only, the demo accounts. **Sign in** leads to the sign-in form. (Its
  **Forgot password?** emails a reset link; after `docker compose up -d` the mail lands in
  Mailpit at `http://localhost:8025`.)
- Sign Ayşe in with a wrong password first: "Invalid email or password." appears under the
  form - the same answer for an unknown email or a locked account.
- Then sign in properly. The dashboard shows today's date and four counts (7 active resources on
  a freshly seeded database - Camera C-1 is in maintenance), then **Today**, **In use now** and
  **Your next reservations**.
- Point out: "today" is the browser's day. Its time zone is sent to the API (ADR-0012), so the
  day boundary is right in Istanbul and on daylight-saving days.

### 2. Find a free room and book it

- **Availability** as Ayşe: `DAY` 13:00 to 14:30, type Meeting Room → Search. Only Active
  resources are listed; Camera C-1 never is. The search is in the URL, so reload keeps it.
- **Book** on Meeting Room A → the booking form opens with the room and times filled in. Add the
  note "Sprint planning", leave **Reminder** at its default, 15 minutes before → **Book** →
  toast, then My reservations. On this first booking with a reminder, the browser asks whether
  SmartDesk may show notifications: allow it, for step 6.
- In Mehmet's window, the booking appears in the Calendar without a reload (SignalR).
- **Month** in Mehmet's Calendar: the booking sits on `DAY` as "Meeting Room A · Ayşe
  Yılmaz". A day's number opens its week. The month view asks the API for the month's own days
  only, so a clock change can't push it past the 42-day limit.

### 3. The brief's conflict example

- As Mehmet, **Availability** for `DAY` 14:00-15:00: Meeting Room A is not listed. For
  14:30-15:30 it is - ranges are half-open, so back-to-back bookings are fine.
- To see the rule enforced on booking, click **+** in Meeting Room A's `DAY` cell of the Calendar
  (it starts a booking of that room), set 14:00-15:00 and **Book**: the red message reads
  "Meeting Room A is already reserved for part of that time.", exactly as the API words it,
  with **See what else is free then** to search that slot.
- Point out: the form checks the obvious rules itself for fast feedback, but the API decides -
  one overlap rule on the server for search, booking, updates and the calendar.

### 4. The form's other rules

- A start more than 5 minutes ago → "The start time is in the past." (the API allows the same
  5-minute grace). End before start, or longer than 31 days, are caught the same way.
- As Ayşe, open Mehmet's entry from the Calendar → read-only: "Only Mehmet Demir or an admin can
  change this reservation."
- A reservation that has already started can only get a new end or note; the room and start are
  locked, with a note saying why.

### 5. Change and cancel

- Ayşe, **My reservations** → **Change** on the Meeting Room A booking → move the end to 15:00 →
  **Save changes**. Mehmet's Calendar shows the new time live.
- **Cancel reservation** on the same form (or **Cancel** in My reservations) → it moves to the
  Cancelled tab, and disappears from Mehmet's Calendar.

### 6. Reminders

- As Ayşe, **New reservation**: Desk 3-01, keep the suggested start (the next full or half
  hour), **Reminder** 30 minutes before → **Book**.
- The start is less than 30 minutes away, so the reminder is already due. Within 30 seconds
  Ayşe's window shows a toast such as "Desk 3-01 starts in 18 min, at 14:00.", and the desktop
  shows a notification that opens the reservation when clicked. Mehmet's window gets nothing:
  a reminder goes to the owner only.
- Point out: a background job looks every 30 seconds for reminders that are due on
  reservations that haven't started. It marks each one sent under the reservation's lock
  before pushing it, so it arrives once, however many tabs are open. Moving the start re-arms
  it (ADR-0018).

### 7. Admin

- In the private window, sign Mehmet out (back to the front page) and sign in as
  `admin@smartdesk.local`. The nav now shows **Admin**. (An employee who types `/admin` lands
  on the dashboard.)
- **Resources** → **New resource**: required fields and limits show inline, and a duplicate
  feature is refused ignoring case. **Deactivate** Meeting Room B → it no longer appears in
  Availability or the booking form, and the Calendar flags it.
- **Admin**: resource counts by status, and every reservation in a date range.
- **Usage report**, in the Admin page's header: the last 30 days by default, with quick ranges.
  Four tiles (the share of office hours booked, hours booked, reservations, cancellations), then
  each resource and type, and who booked the most. Point out: the office day is Monday to
  Friday, 09:00-18:00 in the admin's time zone, so a room booked all day every weekday reads
  100%, not the 27% it would against the whole week (ADR-0017).
- **Blocked windows**, in the Admin page's header: Desk 3-01, keep the suggested times - they
  overlap Ayşe's desk booking from step 6 - reason "Desk repair" → **Block**. It's refused: "1
  confirmed reservation(s) on Desk 3-01 overlap this window. Cancel or move them first." Point
  out: a block never cancels anyone's booking behind their back (ADR-0015).
- **See the reservations then** opens the Admin overview for that day, filtered to desks.
  **Cancel** Ayşe's Desk 3-01 booking - the confirmation and the toast name whose it is, and her
  My reservations updates live.
- Back in **Blocked windows**, the same block again → **Block** goes through and it's listed. In
  Ayşe's window, booking Desk 3-01 inside it says "Desk 3-01 is blocked for part of that time:
  Desk repair", Availability leaves the desk out, and the Calendar draws the block hatched.

## Talking points

- **Structure:** standalone components and signals, one folder per screen, typed API clients in
  `core/services` returning the DTOs from `docs/API_CONTRACTS.md` as-is.
- **Cross-cutting in one place:** two interceptors attach the token and turn every failure into
  one error shape; 0/403/404/5xx raise a toast, 400/409 go to the screen that knows what to say,
  and any 401 ends the session. Route guards hide admin screens, but the API re-checks roles.
- **Live updates:** one hub connection per session (`ReservationHubService`). The dashboard
  refetches on an event, since its numbers come from the server; the Calendar, My reservations
  and Admin patch their lists by id, keep events that arrive mid-load, and refetch after a
  reconnect. Reminders come over the same connection, addressed to the owner only.
- **Times:** picked in local time, sent as UTC, shown back in local time. An unchanged time is
  sent back exactly as the API gave it, since the pickers drop seconds.
- **Tests:** about 290 unit tests, also run in six time zones (`npm run test:time-zones`) - that
  is how a UTC+05:45 half-hour bug was found.
- **Accessibility:** every screen passes an axe-core WCAG 2.1 AA scan. Text colours keep 4.5:1
  contrast on their backgrounds, and the signed-out pages have a main landmark.

## If something goes wrong

| Symptom | Cause / fix |
|---|---|
| Lists stay empty, console shows a certificate or network error | The API isn't on `https://localhost:7065` (started without `--launch-profile https`?), or the dev certificate isn't trusted - see "Before the demo". |
| Changes don't appear in the other window | Check the console for SignalR errors and reload; after a reconnect, screens refetch on their own. |
| No reminder toast | The reservation had already started when the reminder came due, or no window was signed in as the owner at that moment - reminders aren't queued for later. |
| Toast but no desktop notification | The browser's notification permission is blocked for `localhost:4200`; allow it in the site settings. The form's hint says so too. |
| A new block doesn't show in the Calendar | Blocks send no live events - reload, or move to another week and back. |
| "Your session has expired" | Tokens last 60 minutes - sign in again. |
| Old bookings from rehearsals | Clear them as in DEMO.md, step 3. |
