# SmartDesk UI Demo

A 20-minute walkthrough of the Angular app, the companion to [DEMO.md](DEMO.md) (the backend
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
   On a machine with only Docker, this replaces steps 1 and 2 (README, "the whole
   app in Docker"). It serves the same app at the same address:
   ```bash
   export JWT_KEY="<a long random string>"
   docker compose --profile app up -d --build
   ```
   That's a production build, so the front page doesn't list the demo accounts; step 3 does.
3. Open two windows side by side at `http://localhost:4200`: a normal one for **Ayşe**
   (`employee1@smartdesk.local`) and a private one for **Mehmet** (`employee2@smartdesk.local`).
   All passwords are `SmartDesk123!`. Both open on the front page. In Mehmet's window, **Sign
   in** and open **Calendar**. On a wide screen the links run down the left side. The language
   switch, the light/dark switch and the menu under your name are at the bottom of it.
4. The app opens in Turkish. The steps below name the English labels, so press **EN** on the
   front page in both windows, or follow along in Turkish. Step 10 shows the switch.
5. Pick a working day in the current week as `DAY` (or next week, then press **›** in the
   Calendar). Times below are local; 13:00 in Istanbul is DEMO.md's 10:00Z.

## Walkthrough

### 1. Front page, sign in and the dashboard

- Ayşe's window shows the public front page: what SmartDesk does, how booking works, and, in a
  development build only, the demo accounts. **Sign in** leads to the sign-in form. (Its
  **Forgot password?** emails a reset link; after `docker compose up -d` the mail lands in
  Mailpit at `http://localhost:8025`.)
- Sign Ayşe in with a wrong password first: "Invalid email or password." appears under the
  form - the same answer for an unknown email or a locked account.
- Then sign in properly. The dashboard opens with "Welcome, Ayşe Yılmaz", today's date and
  **Find a free resource**. Then come four counts (7 active resources on a freshly seeded
  database - Camera C-1 is in maintenance), then **Today**, **In use now** and **Your next
  reservations**.
- Point out: "today" is the browser's day. Its time zone is sent to the API (ADR-0012), so the
  day boundary is right in Istanbul and on daylight-saving days.
- Point out: nobody gets signed out mid-demo. The token lives in memory for 15 minutes, and the
  app renews it in the background from an HttpOnly session cookie, which no script can read
  (ADR-0026). A reload keeps you signed in, and signing out in one tab signs out the others.

### 2. Find a free room and book it

- **Availability** as Ayşe: `DAY` 13:00 to 14:30, type Meeting Room → Search. Only Active
  resources are listed; Camera C-1 never is. Each card shows the resource's photo, or its type's
  picture until someone uploads one (step 7). The search is in the URL, so reload keeps it.
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

- In the private window, open the menu under Mehmet's name → **Sign out** (back to the front
  page), then sign in as `admin@smartdesk.local`. The nav now shows **Admin** and **Users**. (An
  employee who types `/admin` lands on the dashboard.)
- **Resources** → **New resource**: required fields and limits show inline, and a duplicate
  feature is refused ignoring case. **Deactivate** Meeting Room B → it no longer appears in
  Availability or the booking form, and the Calendar flags it.
- **Edit** Meeting Room A → **Choose a photo** (any JPEG, PNG or WebP on the laptop) → **Save
  changes**. From then on, the list and Ayşe's Availability cards show it. Point out: the app
  shrinks the picture before uploading it, which also drops its location data, and the API checks
  that it really is an image (ADR-0027).
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

### 8. Accounts and teams

- Sign out and open **Sign up** from the sign-in page. Keep **Joining my team**, fill in a new
  person with `manager@smartdesk.local` as the manager's email → **Sign up** → "Check your
  email". Signing in with it says the account is waiting for approval. In Mailpit, Zeynep has
  "… wants to join your team".
- Sign in as `manager@smartdesk.local`: the nav shows **My team**, with a Manager badge. The
  request is at the top → **Approve**, and it moves into the member list.
- **Add a team member** with **Generate** for the temporary password → the new member is listed
  with "Temporary password". Their email says the account exists, but not the password.
- Sign in as that member with the temporary password: the app opens **Choose your own
  password**, and any other address sends them back there. Point out: the API itself refuses
  everything else until then (ADR-0021). After the change they reach the dashboard, and other
  devices are signed out.
- As the manager, **Set password** on that member → their open session gets signed out at once.
- As the admin, **Users**: sign-ups waiting for approval come first. The role select makes an
  employee a manager; the team select moves employees between teams. A manager with members
  can't be demoted until the team is moved.

### 9. Team resources

- As the admin, **Resources**: the **Team** column says Meeting Room B and Projector P-1 are
  "Zeynep Kaya's team", and the rest "Shared". Point out: a resource belongs to one or more
  teams, and no team means everyone's (ADR-0022).
- **Users** → add a second manager, Okan Yıldız, with a temporary password. Sign in as Okan in
  the private window and choose a new password. Okan's **Resources** list has no Meeting Room B
  or Projector P-1, and neither Availability nor the booking form offers them.
- As Okan, **New resource**: there are no team boxes, just a note that it will belong to Okan's
  team. Create "Van" → the row says "Okan Yıldız's team", with **Edit** and **Deactivate**.
  Shared rows have no buttons, and opening one's edit address explains that only an admin can
  change it.
- In Ayşe's window, **Resources** doesn't list the Van: Ayşe is in Zeynep's team.
- As the admin, **Edit** the Van and tick **Zeynep Kaya's team** as well → **Save changes**.
  After a reload Ayşe's **Resources** lists it and it can be booked; its row reads "Okan Yıldız,
  Zeynep Kaya". Okan's **Edit** is gone: a resource that other teams use too is the admins' to
  change.

### 10. Turkish and English, light and dark

- In Ayşe's window, press **TR** (at the bottom of the sidebar, or in the top bar in a narrow
  window). The page reloads in Turkish: the navigation ("Panel", "Kaynaklar",
  "Rezervasyonlarım"), the dashboard date (e.g. "Salı 29 Eylül") and the durations ("1 sa 30 dk").
- **Yeni rezervasyon** → pick a time that overlaps one of Mehmet's bookings. The conflict
  comes back in Turkish too: the app asks the API in the language on screen. Mehmet's window
  is still in English, because the choice is kept per browser.
- **EN** switches Ayşe back. The choice stays after signing out.
- The switch next to it turns on **Dark mode**, and every screen follows at once. A first visit
  follows the system's setting. A reload never flashes white, because the theme is set before
  anything is drawn. Like the language, it's kept per browser.

### 11. Your profile

- In Ayşe's window, the menu under her name has **My profile**, **Change password** and **Sign
  out**. It's a proper menu: the arrow keys move through it, and Escape closes it.
- **My profile** shows her email and role, which an administrator changes, and her name. Change
  the first name to "Ayşe Nur" → **Save changes** → "Your name was saved." The new name shows in
  the menu and on the dashboard at once. Point out: the token is renewed with the new name in it,
  so nobody has to sign in again.
- **Change password**: the eye on a password field shows what's been typed, and hides it again
  when the form is sent. Set the name back to "Ayşe" afterwards.

### 12. The booking assistant (optional)

This step needs an Anthropic API key on the API (`Assistant:ApiKey`, see the README). Without
one there's no **Assistant** button anywhere, and nothing else changes.

- As Ayşe, **Assistant** in the corner → "A room for 4 people tomorrow at 10 for an hour". It
  looks up what's free as Ayşe, and answers with a card: the room, the time and **Reserve**.
- **Reserve** → the card says it's booked and links to My reservations, and the booking appears
  live in Mehmet's Calendar.
- Ask it to cancel that booking → a card with **Cancel reservation**, which asks first, as
  cancelling does everywhere else.
- Point out: the model can only look things up and propose. The server checks each card first,
  and nothing is booked until a person presses its button, through the same endpoint and rules
  as the booking form (ADR-0029).

### 13. Check-in with a QR code

- In a third window, sign in as the admin (`admin@smartdesk.local`), open **Resources** →
  **Board Room** → **Edit**, tick **Bookings must be checked in at the door with a QR code**
  and save. The QR code appears under the box, with **Print QR code** (a sheet with only the
  code, the room and how to use it) and the link it holds.
- As Ayşe, book Board Room for the next half hour. The booking form says the room needs a
  check-in, and **My reservations** shows "Check in at the door" with the times.
- Scanning is opening the link: paste it into Ayşe's window. The page shows her booking and a
  **Check in** button; press it. **My reservations** now says **Checked in**, and Mehmet's
  calendar updates on its own.
- Paste the same link into Mehmet's window: he has no booking there, so the page says so and
  offers to book.
- Point out: a booking nobody checks in to within 15 minutes of its start is released, and
  the owner gets a toast saying why. Switching check-in on doesn't catch bookings made before,
  and switching it off stops releases at once. **Replace code** retires a printed code that
  has got around (ADR-0030).

### 14. Meetings and the inbox

- As the manager (`manager@smartdesk.local`), open **New reservation**, pick Board Room for
  tomorrow, and under **Invite people** tick Ayşe and Mehmet. The hint counts them and says
  each gets a message. **Book**: the toast adds "Your invitations were sent", and **My
  reservations** shows "No answer yet: Ayşe Yılmaz, Mehmet Demir".
- In Ayşe's window the toast arrives at once, and the **Inbox** link (the bell on a narrow
  screen) counts it. Open **Inbox**: "Zeynep Kaya invited you to a meeting", the room and the
  time. Press **I'm coming**. In Mehmet's, press **I can't come**, write why, and **Send**.
- Back as the manager: two messages, "Ayşe Yılmaz is coming to your meeting" and "Mehmet Demir
  can't come to your meeting" with his reason, and **My reservations** says "Coming: Ayşe
  Yılmaz · Can't come: Mehmet Demir".
- Move the meeting to another hour: both get "changed a meeting you're invited to" and are asked
  to answer again. Point out: the reason is only in the manager's inbox, never on the shared
  calendar, and anyone invited may check the meeting in at the door (ADR-0032).

### 15. Running over

- As Ayşe, book a free room from now until eight minutes from now (the form takes a start up to
  five minutes back). Within half a minute a card appears in the corner: "… ends at …", with
  **+15 min**, **+30 min** and **+1 h**, only those the room is free for.
- Press **+30 min**: the toast says until when, the card goes, and Mehmet's calendar shows the
  longer booking on its own. **My reservations** has **Extend** on it while it's on.
- Point out: only the end moves - a check-in still holds, nobody invited has to answer again,
  and they're told it runs longer (ADR-0033).

### 16. A door screen

- As the admin, **Edit** Board Room (check-in on) → **Set up a door screen**. The link appears
  once: open it in a third window, as if on a tablet at the door. The address loses its token,
  and the screen shows the room, free or in use, the rest of today and a QR code, with a bar
  that runs down to its change every 30 seconds.
- As Ayşe, book Board Room from now. The screen shows "In use", waiting for a check-in. Open
  the printed code's link (still on the resource form, now greyed out): **Check in** is refused,
  "Scan the code on the door screen". Now open the link in the screen's QR code: on one
  machine, `document.querySelector('.scan').dataset.url` in the screen window's console gives
  it. **Check in** works, and the screen shows **Checked in** on its next look. A link older
  than a minute is refused, "This code has expired".
- On her check-in page, **We're done: free the room** ends the booking, and the screen turns
  **Free**. Point out: a photo of the screen is good for a minute at most; five minutes before
  a release everyone on the booking is warned; and at a free room the page books it on the
  spot (ADR-0034).

### 17. A weekly meeting

- As Zeynep, **Find a free resource** → Board Room next Monday 10:00-11:00 → **Book**. Under
  **Repeat** choose **Every week**: the last day is four weeks on, and the five Mondays appear
  below, each free. Tick Thursday too: nine dates.
- As Mehmet, book Board Room on one of those Thursdays. Back as Zeynep, change the last day and
  back again: that Thursday is crossed out with why, and the button says **Book the 8 free
  dates**. Invite Ayşe and book.
- Point out: one transaction, under the room's lock, so nothing half-booked; Ayşe's inbox has one
  invitation for the series, answered once for every date ("Your answer is for this date and
  every later one"); in **My reservations** each date says how it repeats, and **Cancel this
  and later dates** ends the series early, with one message to Ayşe (ADR-0035).

## Talking points

- **Structure:** standalone components and signals, one folder per screen, typed API clients in
  `core/services` returning the DTOs from `docs/API_CONTRACTS.md` as-is.
- **Cross-cutting in one place:** two interceptors attach the token and turn every failure into
  one error shape; 0/403/404/5xx raise a toast, 400/409 go to the screen that knows what to say.
  A 401 renews the token once and tries again; only the API saying the session is over signs
  out (ADR-0026). Route guards hide admin screens, but the API re-checks roles.
- **Live updates:** one hub connection per session (`ReservationHubService`). The dashboard
  refetches on an event, since its numbers come from the server; the Calendar, My reservations
  and Admin patch their lists by id, keep events that arrive mid-load, and refetch after a
  reconnect. Reminders come over the same connection, addressed to the owner only.
- **Times:** picked in local time, sent as UTC, shown back in local time. An unchanged time is
  sent back exactly as the API gave it, since the pickers drop seconds.
- **Tests:** over 500 unit tests, also run in six time zones (`npm run test:time-zones`) - that
  is how a UTC+05:45 half-hour bug was found. The browser tests (Playwright) sign in, book,
  clash, watch live updates and scan every screen with axe. CI runs both, and the backend's
  tests, on every pull request.
- **Accessibility:** every screen passes an axe-core WCAG 2.1 AA scan, in both themes. Text
  colours keep 4.5:1 contrast on their backgrounds, and the signed-out pages have a main
  landmark.

## If something goes wrong

| Symptom | Cause / fix |
|---|---|
| Lists stay empty, console shows a certificate or network error | The API isn't on `https://localhost:7065` (started without `--launch-profile https`?), or the dev certificate isn't trusted - see "Before the demo". |
| Changes don't appear in the other window | Check the console for SignalR errors and reload; after a reconnect, screens refetch on their own. |
| No reminder toast | The reservation had already started when the reminder came due, or no window was signed in as the owner at that moment - reminders aren't queued for later. |
| Toast but no desktop notification | The browser's notification permission is blocked for `localhost:4200`; allow it in the site settings. The form's hint says so too. |
| A new block doesn't show in the Calendar | Blocks send no live events - reload, or move to another week and back. |
| "Your session has expired" | The session ended: 14 days without use (30 days at most), a password change, or an admin's change to the account. Sign in again. |
| No **Assistant** button | The API has no `Assistant:ApiKey`. The assistant is optional, and everything else works without it. |
| Old bookings from rehearsals | Clear them as in DEMO.md, step 3. |
