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
5. Optional, for an office that looks lived in: before step 1's `dotnet run`, run the API
   once with `-- --demo-data` (README, "A lived-in office for a demo"). The usage report then
   has six weeks to show, every floor has its plan and resources on it, and the calendars are
   busy. The steps below still work: Meeting Room A and the Board Room are kept free from now
   on. Counts differ - 28 active resources rather than 7 - and the floors already have plans,
   which step 20's upload replaces.
6. Pick a working day in the current week as `DAY` (or next week, then press **›** in the
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
- Under it, **Free at another time** has the hour just before and the hour just after Ayşe's
  booking, and **Free at that time** the other meeting rooms free then, the ones as big first.
  Choose the hour after: the form takes it, and **Book** books it.
- Point out: the form checks the obvious rules itself for fast feedback, but the API decides -
  one overlap rule on the server for search, booking, updates and the calendar. The
  suggestions come with the same 409, worked out once the booking has failed, so they hold
  nothing and the next try checks again (ADR-0042).

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
  Tiles for the share of office hours booked, hours booked, reservations, cancellations, the
  share of bookings cancelled and the share of check-ins missed; then each resource and type,
  **When it's busiest** - a heat map of weekdays and hours, darker where more of the resources
  were booked - and who booked the most. Point out: the office day is Monday to Friday,
  09:00-18:00 in the admin's time zone, so a room booked all day every weekday reads 100%, not
  the 27% it would against the whole week (ADR-0017); a missed check-in is one released after
  step 13's grace, so it counts what the rooms lost, not who forgot (ADR-0044).
- **Download as Excel**: the same report as a workbook - a summary, then resources, types,
  people and the hours, each a sheet, the hours coloured as on the page. Switch the app to
  Turkish and download again: the sheets and headings are Turkish too, and the numbers are
  numbers Excel can sort and sum (ADR-0045).
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
  **Check in** button; press it. **My reservations** now says **Checked in**, and at once, with
  no reload, Mehmet's calendar ticks the booking (✓, its tooltip says when) and his dashboard
  marks it **Checked in**.
- Paste the same link into Mehmet's window: he has no booking there, so the page says so and
  offers to book.
- A release, while the audience watches: start the API with `CheckIn__GraceMinutes=1`
  beforehand (ADR-0040; 15 minutes otherwise). As Mehmet, book Board Room from the next quarter
  hour and don't check in. At the start a toast warns him that nobody has checked in yet, and
  the time it's released, a minute later. Then the booking leaves the calendars, and he gets a
  toast saying why. Every text in the app gives the setting's times.
- Point out: a booking nobody checks in to within its grace after the start is released, and
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

### 18. The guide

- **Guide** at the bottom of the menu: the app in short parts, each with a link to its page.
  Sign in as Zeynep and then as the admin: each sees the part for their role, and the assistant's
  part appears only on a server with an assistant.

### 19. A problem at the door

This one is best on a phone: see "On a phone" in the frontend README. On one machine, a third
window does.

- As the admin, **Resources** → **QR labels**: every resource's code, eight to an A4 sheet with
  lines to cut along, check-in or not. Print one, or show the print preview. Each opens its
  resource's page.
- As Mehmet, open Board Room's code (on its form, under **Check-in**, or scanned from a label).
  The page has the room as its title, with where it is, how many it holds and what it has, and
  under it what the room is doing now - free, in use until when, or not available and why, as
  its door screen says - then its day, and **Problems here**. **Report a problem** →
  **Screen or TV**, "The TV doesn't turn on", a photo if on a phone (it opens the back camera),
  tick **It can't be used until this is fixed** → **Send the report**.
- The admin's window shows a toast at once, and **Problems** in the menu counts 1. Open it: the
  report is on top, marked as unusable, with its photo.
- As Ayşe, open the same code: the problem is listed, and choosing **Screen or TV** says it's
  already reported instead of sending it again.
- Back as the admin: **Take it out of use** → **the end of today**. It says first how many
  bookings that cancels → **Close it**. The Calendar shows the window blocked with the problem
  as its reason, and everyone who had a booking in it has a message in their inbox saying why.
- **Mark resolved**: Mehmet's inbox says the problem he reported is resolved, and the screen asks
  whether to open Board Room again → **OK**.
- Point out: one report per problem (the same kind on the same resource while it isn't fixed,
  except "Something else"); whoever may manage the resource follows it up - the admins, or the
  manager of the one team it belongs to; photos are kept like a resource's, made smaller on the
  phone first; and a door screen's codes are keyed by the screen, so the printed code can't
  compute them (ADR-0037, ADR-0038, ADR-0039).

### 20. The floor plan

Have a picture of a floor at hand: a PNG exported from draw.io, or a photo of the evacuation
plan by the lift.

- As the admin, **Floor plan** in the menu, under Calendar → **2nd Floor**. Without a plan, its
  rooms stand on a grid and are listed under it as not on the plan yet.
- **Arrange** → **Upload a plan**: it's made smaller on the way up, and the floor takes the
  picture's shape. **Put on the plan** for Meeting Room A and B, drag each to its place, and
  nudge one with the arrow keys (Shift for bigger steps) → **Done arranging**.
- As Ayşe, **Floor plan** → **2nd Floor**: green is free, red in use, grey and dashed out of use;
  pointing at one says until when. As Mehmet, book Meeting Room A from now: in Ayşe's window it
  turns red at once.
- Choose a room: the booking form opens for it from the next quarter hour, for an hour, or from
  when it's free again. **At** shows the floor at another time, tomorrow at 10:00 say, and
  **Now** comes back.
- As Zeynep, **Arrange** offers only Meeting Room B, her team's, and no upload: the plan is the
  admins'.
- Point out: a floor is the location its resources already share, so there's nothing new to set
  up; places are kept as fractions of the plan, so a sharper picture replaces it without moving
  anything; and changing a resource's location takes it off the plan (ADR-0041).

### 21. The waiting list

- As Ayşe, book Board Room for `DAY` 15:00-16:00. As Mehmet, try the same time: under the
  message, **Or wait for it** → **Join the waiting list**. My reservations opens with **Waiting
  for**: Board Room, `DAY` 15:00-16:00, number 1 in line.
- As Ayşe, cancel it. In Mehmet's window a message comes at once - "A time you were waiting for
  freed up, and it's booked for you" - the booking is in his list, and the waiting card is gone.
  Ayşe's calendar shows Board Room booked again, by Mehmet.
- Point out: it's booked, not just announced, so whoever asked first gets it; the same happens
  when a booking is released because nobody checked in, ended early or moved, or a block is
  lifted. Mehmet cancelling would offer it to the next in line (ADR-0043).

### 22. Booking rules

- As the admin, **Admin** → **Booking rules**. Office hours are Mon-Fri 09:00-18:00 in
  Europe/Istanbul; each kind of resource has empty boxes: no rules of its own yet.
- On **Desk**: longest booking `9` hours, `7` days ahead, `2` bookings per person, tick
  **Within office hours only**, **Save changes** → "Rules for Desk saved."
- As Ayşe, **New reservation**, pick Desk 3-01: under it, "Desk bookings: within office hours
  (Mon-Fri 09:00-18:00), at most 9 h long, up to 7 days ahead, at most 2 at a time per person."
  Choose 19:00-20:00 and **Book**: "Desk bookings must be within office hours: Mon-Fri
  09:00-18:00." under the time, and nothing is sent. A date eight days on says the same about
  days ahead.
- Book desks for tomorrow until Ayşe has two that haven't ended; the next one says "Desk: at most
  2 upcoming bookings per person. Cancel one first." That one comes from the API, which counts
  them under a lock, so two at once can't both get through.
- Back as the admin, clear the Desk boxes and save, for the next demo. Point out: one policy
  applies the rules to every way of booking - the form, a series, extending, the waiting list,
  the assistant - and the search and suggestions only offer what they allow (ADR-0046).

### 23. In your own calendar

- As Ayşe, **My reservations** → **Add to calendar** on an upcoming booking: an `.ics` file
  downloads, and opening it adds the booking to Outlook or the computer's calendar, with the
  room, the floor, the note and a link back to SmartDesk.
- **Calendar link** at the top of the page → the profile's **Your calendar** → **Make my
  calendar link**. The link is shown this once: **Copy link**, **Open in my calendar app** or
  **Add to Google Calendar**. Below, "No calendar has fetched it yet."
- Subscribe to it in a calendar app (Outlook: Add calendar → Subscribe from web). After its
  first fetch, the profile says when a calendar last fetched it. Book, move or cancel something
  in SmartDesk: the calendar follows at its next fetch.
- Point out: one link per person, kept only as its hash; **New link** cuts off the old one at
  once. Google Calendar and Outlook on the web fetch from the internet, so for them the app has
  to be on the internet too: a tunnel does it (frontend README, "On a phone"; ADR-0048).

### 24. A car that waits for approval

- As the admin, **Admin** → **Booking rules**, on **Company Car** tick **Wait for an Admin's
  approval**, **Save changes**.
- As Ayşe, **New reservation**, Company Car 34 SD 001 on a working day after today,
  09:00-17:00: under it, "Company Car bookings: approved by an Admin first.", no **Repeat**, and
  the button says **Ask for it**. Press it: "… is asked for …". My reservations has it under
  Upcoming, **Awaiting approval**; as Mehmet, the car at that time is taken.
- As the admin, the inbox says "Ayşe Yılmaz asks for a booking that needs your approval", and
  **Admin** shows **Approvals 1**. Open it: the car, the time, who asked and why. **Approve** -
  Ayşe's list says "Approved by …" at once, and her inbox says so. Ask for another, and this time
  **Decline** with a reason: her Cancelled tab shows it.
- Point out: a request holds its time, so the first to ask is first in line and an approval never
  meets a clash; until approved it has no reminder or check-in and isn't in the report; one
  nobody answers lapses at its start. Untick the box afterwards, for the next demo (ADR-0049).

### 25. A phone with no account

- Best on a phone that has never signed in, through the tunnel ("On a phone" in the frontend
  README): scan Board Room's code from step 13, or one on **QR labels**. On one machine, paste
  the link into a private window.
- The room's own page opens, not the sign-in form: where it is, what it has, whether it's free
  or in use and until when, as its door screen says it - times only, nobody's name.
- With a booking of Board Room starting within ten minutes, Ayşe's say: "The booking from …
  is waiting for its check-in" and **Check in**. Press it: the room is kept for it, and Ayşe's
  **My reservations** says **Checked in** at once.
- **Report a problem**, as in step 19: it goes in without an account, and on **Problems** the
  admin sees "Reported at the door, without an account".
- **Sign in** at the bottom comes back to the same page, signed in, with booking on the spot.
- Point out: the code shows someone is at the door, and that's what a check-in is for, so no
  account is needed - but then a photo of a printed code checks in anyone's booking, which a
  door screen's changing code stops (ADR-0050).

### 26. Favourites

- As Ayşe, **Resources**: every row has a star. Star Meeting Room A and Board Room, then tick
  **Favourites only**: those two are left.
- The dashboard has **Your favourites**, each saying what it's doing now - free until when, in use
  until when, or not available - with **Book**, which opens the form with it picked. In the form,
  the favourites come first among the resources.
- Point out: they're kept with the account, so the phone has the same stars; and only what she
  may use can be starred (ADR-0051).

### 27. Who changed what

- As the admin, **Admin** → **History**: every change, the newest first - what and which, who
  and when, and each field before and after. The steps above are in it: Ayşe's bookings, the
  car approved, a problem reported "by someone without an account", a booking nobody checked in
  to released by "SmartDesk, by itself".
- **What** → **Bookings**, a person under **Who**, or some days under **From** and **To** narrow
  it; **Show older** goes further back.
- From **Resources**, open Board Room's edit page: **History** beside **Back to resources** shows
  only Board Room's, with its bookings. Change its capacity, save, and look again: the old number
  and the new.
- Point out: the saves write it themselves, in the same transaction, so no way of changing
  something is left out and nothing rolled back is in it; a password or a QR code only shows that
  it changed (ADR-0052).

### 28. On the home screen

- Run a production build: the whole app in Docker ("the whole app in Docker" in the README), or
  `npx ng build` and `npm run serve:build` in place of `ng serve` (frontend README). Open it on a
  phone through a tunnel to its port, as in step 25, or in Chrome or Edge on the computer.
- Chrome's menu: **Install app**; on an iPhone, **Share**, then **Add to Home Screen**. It opens
  from the home screen on its own, without the browser's bars.
- Turn on airplane mode and open it again: it opens, saying it's offline, and nothing can be
  booked until the network is back.
- Point out: it keeps the app, never the bookings, which always come from the server; and a new
  release asks to reload, while a door screen takes it by itself (ADR-0054).
- On the phone, the bar at the bottom: the dashboard, the floor plan, **Book** in the middle and
  your bookings. **Menu** slides up with every other screen, and a dot on it when there's
  something unread. The floor plan keeps its size and scrolls sideways; the status bar takes the
  top bar's colour, in either theme (ADR-0055).

### 29. Notifications on the phone

- The API needs its VAPID keys ("Optional: notifications on phones and browsers" in the README).
  On the phone, in the app opened from its home screen, as Ayşe: **Profile**, then
  **Notifications**: **Turn on for this device**, and allow them when the phone asks. **Send a
  test**: it arrives on the lock screen within seconds.
- Lock the phone. On the computer, as the manager, book a room for 30 minutes from now inviting
  Ayşe: the invitation reaches her lock screen at once, and tapping it opens her inbox. Her own
  booking with a 15-minute reminder lights the phone up 15 minutes before it starts.
- Point out: the notification is encrypted for the phone alone, so Apple's push service carries
  it without being able to read it; the API sends it itself, with no account at Apple or Google
  (ADR-0056). **Sign out** turns them off for that phone.

### 30. Face ID instead of a password

- On the phone, at the tunnel's address the API has as `App__FrontendUrl`, as Ayşe: **Profile**,
  then **Passkeys**. The name already says "iPhone": type her password, **Add a passkey**, and
  look at the phone when Face ID asks. It's listed, "synced to your other devices".
- **Sign out**, and on the sign-in page tap **Sign in with a passkey**: the phone offers Ayşe's
  passkey, Face ID again, and the dashboard opens. No email, no password.
- On the computer, at `http://localhost:4200`, Windows Hello does the same. A passkey belongs to
  the address it was made at, so the computer's and the phone's are two.
- Point out: the passkey signs only for SmartDesk's own address, so a look-alike site gets nothing
  it can use; the server keeps only the public key; adding one asks for the password, so a stolen
  session can't plant one; and the checks are ASP.NET Core Identity's own, new in .NET 10
  (ADR-0057). As the admin, **History** shows the passkey added under its name.

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
- **Tests:** over 900 unit tests, also run in six time zones (`npm run test:time-zones`) - that
  is how a UTC+05:45 half-hour bug was found. The browser tests (Playwright) sign in, book,
  clash, watch live updates and scan every screen with axe. CI runs both, and the backend's
  tests, on every pull request.
- **Accessibility:** every screen passes an axe-core WCAG 2.1 AA scan, in both themes. Text
  colours keep 4.5:1 contrast on their backgrounds, and the signed-out pages have a main
  landmark.
- **Logs:** each request is a line in Seq (`http://localhost:5341`, `docker compose up -d seq`)
  with its status, time and user, and every value in a message a field: `StatusCode >= 400`
  lists what went wrong, newest first, and a `UserId` everything one person did (ADR-0053).
- **A limit, said up front:** a photo of a printed QR code checks in from anywhere, as long as
  the code isn't replaced, and with no account needed at the door, anyone's booking (ADR-0050);
  checking in proves you hold the code, not that you're at the door.
  **Replace code** retires a code that got around, and a door screen closes the gap: its code
  changes every 30 seconds, so a photo of it works for a minute at most (ADR-0030, ADR-0034,
  ADR-0039).

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
| Something failed and the screen doesn't say why | Find the request in Seq at `http://localhost:5341` (`docker compose up -d seq`): `StatusCode >= 400` lists the failures, newest first, each with its user, and its `RequestId` the lines written meanwhile (ADR-0053). |
| The phone can't open a QR code | The code carries the API's `App:FrontendUrl`, `localhost` by default, which a phone can't reach. Open a tunnel, which a phone reaches from any network, or use the computer's IP on the same Wi-Fi, and set `App__FrontendUrl` to that address (frontend README, "On a phone"). |
| On a phone, a reload asks to sign in again | Over plain http the phone doesn't keep the session cookie, which is `Secure`. A tunnel gives https and keeps it. |
| Old bookings from rehearsals | Clear them as in DEMO.md, step 3. |
