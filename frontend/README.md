# SmartDesk frontend

Angular 22 (standalone components, signals, Reactive Forms) for the SmartDesk API. The
architecture - folders, interceptors, guards, how times are handled - is in
[ARCHITECTURE.md, section 7](../docs/ARCHITECTURE.md#7-frontend-architecture); this page is
about working on it.

## Run it

Start the API first, with the `https` launch profile. That serves `https://localhost:7065`,
where the dev server sends the app's API calls:

```bash
dotnet run --project src/SmartDesk.API --launch-profile https   # from the repository root
```

Then:

```bash
cd frontend
npm install
ng serve        # http://localhost:4200
```

Sign in with a demo account: `employee1@smartdesk.local`, `employee2@smartdesk.local`,
`manager@smartdesk.local` or `admin@smartdesk.local`, all with `SmartDesk123!`. They exist in
Development only, and the front page lists them there.

The app always calls `/api` and `/hubs` on its own origin. `ng serve` proxies them to the API,
so the session cookie is a same-site cookie in development too (ADR-0026):

| Configuration                                | Proxied to                                    | When                                                     |
| -------------------------------------------- | --------------------------------------------- | -------------------------------------------------------- |
| `development` (the `ng serve` default)       | `https://localhost:7065` (`proxy.conf.json`)  | the API from `dotnet run`                                |
| `docker` (`ng serve --configuration docker`) | `http://localhost:8080` (`proxy.docker.json`) | the API container from `docker compose --profile api up` |
| `production` (`ng build`)                    | nothing - `/api` on the same host             | served behind the API or a reverse proxy                 |

`frontend/Dockerfile` serves the `production` build with nginx, which passes `/api` and `/hubs` on
to the API container: `docker compose --profile app up -d --build` runs the whole app at
`http://localhost:4200` (ADR-0031, and "the whole app in Docker" in the main README).
`nginx.conf` is its configuration.

If the lists stay empty and the console shows a certificate error, the browser doesn't trust
the dev certificate yet: `dotnet dev-certs https --trust`.

### On a phone

A resource's QR code opens its page on the phone that scans it (ADR-0030, ADR-0037), so it's
worth trying on a real one. A phone can't reach `localhost` on the computer; give it an address
it can reach, and start the API with `App__FrontendUrl` set to that address, so the QR codes it
makes point there. Either way only the dev server's port is reached: it passes `/api` and `/hubs`
on to the API.

- **Any network, through a tunnel:** a tunnel gives the dev server an https address on the
  internet, which a phone opens from any Wi-Fi or over mobile data; over https it keeps the
  session cookie, too. Cloudflare's quick tunnel needs no account, only `cloudflared`, a single
  program ([releases](https://github.com/cloudflare/cloudflared/releases), or
  `winget install Cloudflare.cloudflared`):

  ```bash
  cloudflared tunnel --url http://localhost:4200 --http-host-header localhost:4200
  ```

  It prints the address, `https://<four random words>.trycloudflare.com`: that is
  `App__FrontendUrl`. `--http-host-header` passes each request on as one for `localhost`, which
  the dev server answers; a tunnel that keeps its own host name needs that name in
  `allowedHosts` (the `serve` options in `angular.json`) instead. Every start of `cloudflared`
  gives a new address. While the tunnel is open, anyone who has the address reaches the app,
  and a Development build lists its demo sign-ins on the front page: close it when you're done.

- **The same Wi-Fi:** `ng serve --host 0.0.0.0`, then open `http://<the computer's IP>:4200` on
  the phone (`ipconfig` shows the IP; Windows Firewall has to let port 4200 in). A guest network
  may keep its devices from reaching each other. Over plain http the phone doesn't keep the
  session cookie, which is `Secure` (ADR-0026), so a reload asks you to sign in again.
- A printed code keeps the address it was made with: print the ones for the doors once the app
  has its final domain.

## Before a pull request

```bash
npx ng build
npm run test:time-zones                        # all unit tests, once per time zone
npx tsc -p tsconfig.spec.json --noEmit         # type-checks the specs too
npm run format:check                           # formatting
npm run i18n:check                             # every text has its Turkish version
```

CI runs all of these on every pull request, and the browser tests too (see Browser tests).

- **`test:time-zones`** runs the whole suite in six zones: Istanbul, New York, Berlin, Tokyo,
  Sydney and Kathmandu (UTC+05:45). Date code that passes in one can fail in another around a
  clock change or a half-hour offset. `npm run test:time-zones -- Asia/Kolkata` runs just the
  zones given. Setting `TZ=...` in front of `ng test` doesn't work in Git Bash on Windows; the
  script sets it for each run itself. CI runs it on every pull request.
- **`tsc -p tsconfig.spec.json`:** `ng test` compiles specs without checking their types, so a
  spec that builds a model with a missing field still runs.
- **`format:check`** checks the code, the configuration and `public/i18n` with Prettier.
  `.prettierrc` keeps each file's line endings (`endOfLine: auto`), since Windows checkouts
  have CRLF and Prettier would otherwise flag every file.

## Screens

| Route                                           | Who                         | What                                                                                                                                                                                                                                                                                                                                                                             |
| ----------------------------------------------- | --------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `/`                                             | visitors                    | Front page; signed-in users go to the dashboard                                                                                                                                                                                                                                                                                                                                  |
| `/login`, `/forgot-password`, `/reset-password` | anyone                      | Sign-in, and a password reset by emailed link                                                                                                                                                                                                                                                                                                                                    |
| `/register`                                     | visitors                    | Sign-up: a team member gives their manager's email and waits for that manager; a manager waits for an admin                                                                                                                                                                                                                                                                      |
| `/change-password`                              | signed in                   | Change your password. Forced, before anything else, after signing in with a temporary password                                                                                                                                                                                                                                                                                   |
| `/profile`                                      | signed in                   | Your own name, which you can change, and your email and role, which an admin changes. Open it, the password page or sign out from the menu under your name: at the bottom of the sidebar, or in the top right on narrower screens                                                                                                                                                |
| `/dashboard`                                    | signed in                   | Today's reservations, what's in use now, your next bookings                                                                                                                                                                                                                                                                                                                      |
| `/resources`                                    | signed in                   | Your team's resources and the shared ones; admins see every resource. Each row says whose team it belongs to, or "Shared"                                                                                                                                                                                                                                                        |
| `/resources/new`, `/resources/:id/edit`         | Admin, Manager              | Admins create and edit any resource and pick the teams that may use it (none: shared by everyone). A manager adds resources to their own team, and edits, activates and deactivates only the ones that belong to their team alone (ADR-0022). A saved resource shows its QR code here (ADR-0030, ADR-0037); with check-in switched on, its door screen too                       |
| `/resources/qr-labels`                          | Admin, Manager              | The QR codes of many resources as labels, eight to an A4 sheet with lines to cut along, picked one by one or by type (ADR-0037). Outside the shell, so only the labels print                                                                                                                                                                                                     |
| `/resources/:id/check-in-poster`                | Admin, Manager              | The QR code on a sheet to print and put up at the door. Outside the shell, so only the sheet prints                                                                                                                                                                                                                                                                              |
| `/check-in/:resourceId?code=…`                  | signed in                   | Where a resource's QR code leads: the resource's own page at the door. Your booking of it, and a button to check in while that's open. The code leaves the address once read; at a free room, book it on the spot; checked in, run longer or end it (ADR-0034). Anyone there sees the problems already reported and reports one, with a photo from the phone's camera (ADR-0037) |
| `/door/:resourceId#<token>`                     | the door screen, no sign-in | The tablet at a resource's door: free or in use, today's bookings as times, and a QR code that changes every 30 seconds (ADR-0034)                                                                                                                                                                                                                                               |
| `/availability`                                 | signed in                   | What's free for a time range, by type and capacity                                                                                                                                                                                                                                                                                                                               |
| `/calendar`                                     | signed in                   | The shared office calendar: who booked what                                                                                                                                                                                                                                                                                                                                      |
| `/floor-plan?floor=…&at=…`                      | signed in                   | A floor's plan with its resources on it: green free, red in use, grey out of use, now or at a chosen time, and live. Choosing one opens the booking form from when it's free. Admins upload a floor's plan and place any resource, by dragging it or with the arrow keys; a manager places their team's own (ADR-0041)                                                           |
| `/reservations/new`, `/reservations/:id/edit`   | signed in                   | Book, change or cancel; managers and admins invite people (ADR-0032). A new booking can repeat, its dates listed first (ADR-0035). Read-only for anyone but the owner or an admin, where someone invited answers                                                                                                                                                                 |
| `/reservations/mine`                            | signed in                   | Your upcoming, past and cancelled reservations, and the meetings you're invited to. A booking in progress can be extended here (ADR-0033)                                                                                                                                                                                                                                        |
| `/guide`                                        | signed in                   | How SmartDesk works: each part of the app in a few steps, with a link to where it's done; a manager's and an admin's parts for them only, the assistant's only when the server has one. Each part has its own address (`/guide#door`)                                                                                                                                            |
| `/inbox`                                        | signed in                   | Your messages: invitations to answer, meetings moved or cancelled, and who's coming to yours. The **Inbox** link counts the unread ones                                                                                                                                                                                                                                          |
| `/admin`                                        | Admin                       | Resource counts, and every reservation in a date range to change or cancel                                                                                                                                                                                                                                                                                                       |
| `/admin/reports`                                | Admin                       | Usage report                                                                                                                                                                                                                                                                                                                                                                     |
| `/admin/blocks`                                 | Admin                       | Blocked windows: take a resource out of booking for a while                                                                                                                                                                                                                                                                                                                      |
| `/issues`                                       | Admin, Manager              | Reported problems on the resources you look after, coming in live: take them up, resolve them (the reporter is told), and close a resource until a chosen time, cancelling the bookings in it, then open it again (ADR-0037, ADR-0038). The **Problems** link counts the ones nobody has taken up                                                                                |
| `/admin/users`                                  | Admin                       | Every account: approve or reject sign-ups, add accounts with a temporary password, change roles and teams, set temporary passwords, disable and enable                                                                                                                                                                                                                           |
| `/team`                                         | Manager                     | Your team: approve or reject requests to join, add members with a temporary password, set temporary passwords, disable, enable, remove                                                                                                                                                                                                                                           |

The guards only hide screens. The API checks the role on every request.

## Languages

The UI is in Turkish and English, Turkish unless the user picks English with **TR | EN** (top
bar, front page, sign-in). The choice is kept per browser, and switching reloads the page
(ADR-0025). Dates and numbers follow the language, and so do the API's messages, since
`languageInterceptor` sends the app's `LOCALE_ID` as `Accept-Language`.

- **English is written in the code, Turkish in `public/i18n/tr.json`.** Mark template text
  with `i18n="@@screen.key"`, and attributes with `i18n-placeholder` / `i18n-aria-label`.
  In code, use `$localize`, with a name for each placeholder:
  `` $localize`:@@team.removed:${member.displayName}:name: is no longer in your team.` ``.
- **After adding or changing a text**, run `npm run i18n:extract`. It updates
  `src/locale/messages.json` and lists every id that `tr.json` lacks or no longer needs; add
  those. `npm run i18n:check` changes nothing and fails if the two are out of step, and so
  does a unit spec. Give a text a new id when its meaning changes, so the old Turkish doesn't
  stay.
- **Write whole sentences, one per case.** Don't build them from pieces ("a"/"an", "X's",
  `count === 1 ? 'block' : 'blocks'`): Turkish orders the words differently. Put the choice
  in code, and give each case its own message.
- **In Turkish, never put a suffix on a placeholder** ("{$name}'in" depends on the name):
  "{$name} adlı kişinin …", "{$manager} ekibi", "bitiş {$time}".
- **Words:** the same as the API's (ADR-0023): parola, kaynak, rezervasyon, kapatma (blocked
  window), ekip, ekip yöneticisi (Manager), sistem yöneticisi (Admin), çalışan (Employee).
- **Values the API sends as data** (a status, a role) go through `core/i18n/labels.ts`.
- **Format dates with `DatePipe` or `formatDate(…, this.locale)`**, never
  `toLocaleString()` without a locale.
- Unit tests run in English, the language of the code.

## The assistant

`layout/assistant` holds the booking assistant (ADR-0029). The shell shows its button in the
bottom corner, but only when `GET /api/assistant` says the server has one. It needs no setting
in the app. The conversation is kept in the component until sign-out, and every question sends
all of it. Replies are plain text, shown as paragraphs and lists (`reply-format.ts`), never as
HTML. Cards book and cancel through `ReservationService`, so errors, live updates and "My
reservations" behave as they do on the screens.

The browser tests answer for the model with `page.route` (`e2e/assistant.spec.ts`), so they
need no key. The booking a card makes still goes to the real API.

## Look and feel

- **Brand:** indigo into violet. `--gradient-brand` fills the logo (`shared/components/logo`),
  primary buttons and avatars; the deeper `--gradient-deep` is for large areas with white text,
  through the `.brand-panel` class (the dashboard's greeting, the front page's closing panel,
  the sign-in pages' showcase). `.btn-light` is the button for those panels.
- **Typeface:** Plus Jakarta Sans, bundled with the app from `@fontsource-variable` (no request
  to a font service; only the Latin subsets a page needs are downloaded), with the system font
  as the fallback.
- **Icons:** `<app-icon name="…">` draws one of a small set of line icons in the text's colour.
  They're decoration: whatever an icon stands for is written next to it. Add one to the list in
  `icon.ts`.
- **Layout:** signed-in screens get a sidebar from 1100px, and a bar on top below that. The
  public pages (front page, sign-in, sign-up, password pages) share `<app-backdrop>`, a slow
  drift of the brand's colours; on wide screens the account pages put `<app-auth-showcase>`
  beside the form.
- **Bars on top** (the app's below 1100px, the front page's) get out of the way while the page
  scrolls down and come back as soon as it scrolls up, so the navigation is one flick away on a
  long page. They're always there at the top and the very bottom of a page, and whenever
  something in them has focus. The `appHideOnScroll` directive (`shared/directives`) sets
  `scrolled-away`; the bar's styles slide it up.
- **Motion:** screens rise in as they open, the front page's hero line by line. Hover lifts
  cards and buttons a little. With "reduce motion" on, nothing moves (`styles.scss`).
- **Style budgets:** a component's styles stay under 4 kB (`angular.json`). When one grows
  past that, move a part that stands on its own into a component (the front page's hero and
  its preview are their own), or a pattern that several screens use into `styles.scss`.

## Light and dark

The moon/sun button next to **TR | EN** switches between a light and a dark theme. Until a
user picks one, the app follows the system's setting, live. The pick is remembered per
browser, and other open tabs follow it.

- **Colours are tokens.** `src/styles.scss` defines them on `:root` and again, for dark, on
  `:root[data-theme='dark']`. Use `var(--color-…)`, never a colour value, and a new token
  needs a dark value too. Text on a primary background uses `--color-on-primary`, because the
  dark theme's primary indigo is light.
- **Contrast:** every text colour keeps at least 4.5:1 on the backgrounds it sits on, in both
  themes. `e2e/screens.spec.ts` runs axe on every screen in each theme, once the screen's
  entrance animation is over. axe can't judge text on a gradient or on the drifting backdrop,
  so keep those colours to the ones already checked: white on `--gradient-deep`, and on the
  backdrop nothing fainter than the front page's lead (muted text mixed with a third of the
  text colour).
- **No flash of the wrong theme:** a small script in `index.html` sets `data-theme` before
  anything is drawn. `ThemeService` (`core/theme`) takes over from there.

## Browser tests

`e2e/` holds [Playwright](https://playwright.dev/) tests that drive the real app in a browser:
sign-in and the language, booking with a conflict, a booking reaching another window's calendar
live, a blocked window, a manager's team resource, joining a team through the manager, and a
temporary password that has to be replaced. `screens.spec.ts` also opens every
screen in Turkish, in the light and the dark theme, runs
[axe](https://github.com/dequelabs/axe-core) on it (WCAG 2.1 AA and best practices), and checks
that nothing is wider than a 390px phone. They need the whole app
running with the Development seed:

```bash
dotnet run --project src/SmartDesk.API --launch-profile https   # from the repo root
npx ng serve                                                    # from frontend/
npm run e2e                                                     # about a minute
```

- **Browser:** the Edge installed on the machine, so there's nothing to download. Set
  `E2E_CHANNEL=` (empty) to use Playwright's own Chromium instead, after
  `npx playwright install chromium`.
- **Other addresses:** `E2E_BASE_URL` (default `http://localhost:4200`), `E2E_API_URL`
  (`https://localhost:7065/api`) and `E2E_PASSWORD` (the seed's).
- **Data:** each test makes its own resource or account through the API. At the end it cancels
  that resource's bookings and deactivates it, or takes the account out of its team and
  disables it: used ones can't be deleted, and a sign-up still waiting is rejected, which
  deletes it. A local database collects inactive "E2E …" resources and disabled
  `e2e-…@example.com` accounts over time; they don't affect anything else, and the demo
  users are left as seeded.
- **A failure** keeps a trace in `test-results/`: `npx playwright show-trace <file>` replays it
  step by step.
- **In CI** they run on every pull request, in Playwright's Chromium against a fresh database
  (the `e2e` job in `.github/workflows/ci.yml`). A failed run uploads the report, the traces and
  the API's and `ng serve`'s logs as the `browser-tests` artifact.
- **Writing one:** use the fixtures in `e2e/support.ts`: `open('ayse')` for a signed-in window,
  `resource` for a fresh resource, and `admin` for the API. Wait on what the user sees
  (`expect(...).toBeVisible()`), never on a timeout. Type-check with
  `npx tsc -p e2e/tsconfig.json`.

## Writing tests

Specs sit next to the code (`*.spec.ts`) and run on [Vitest](https://vitest.dev/) through
`ng test`. What the existing ones rely on:

- **Services are faked, not HTTP.** A component spec provides `useValue` fakes of the services
  it uses, returning `of(...)`, a `Subject` or `throwError(() => new ApiError(...))`. Service
  specs use `HttpTestingController`.
- **Time is fixed.** Call `vi.useFakeTimers({ toFake: ['Date'] })` and `vi.setSystemTime(...)`
  with a local wall-clock time. Build API timestamps with
  `const utc = (local: string) => new Date(local).toISOString()`, so a test means the same
  thing in every zone.
- **Fixtures** in `src/testing`:
  - `fakeReservation()` and `fakeResource()` build models
  - `fakeJwt()` builds a token with the claims the API issues
  - `simulateTurkishBrowser()` applies Turkish casing ("I" → "ı"), which Node doesn't do
    even with a Turkish locale
- **Assert what the user sees:** the text and `href`s in the rendered DOM, and the requests
  sent.
- **Check the test can fail.** Break the line it's about and make sure it goes red.
