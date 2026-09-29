# SmartDesk frontend

Angular 22 (standalone components, signals, Reactive Forms) for the SmartDesk API. The
architecture - folders, interceptors, guards, how times are handled - is in
[ARCHITECTURE.md, section 7](../docs/ARCHITECTURE.md#7-frontend-architecture); this page is
about working on it.

## Run it

Start the API first, with the `https` launch profile. That serves `https://localhost:7065`,
the address the development build calls:

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

| Configuration | API it calls | When |
|---|---|---|
| `development` (the `ng serve` default) | `https://localhost:7065/api` | the API from `dotnet run` |
| `docker` (`ng serve --configuration docker`) | `http://localhost:8080/api` | the API container from `docker compose --profile api up` |
| `production` (`ng build`) | `/api` on the same host | served behind the API or a proxy |

If the lists stay empty and the console shows a certificate error, the browser doesn't trust
the dev certificate yet: `dotnet dev-certs https --trust`.

## Before a pull request

```bash
npx ng build
npm run test:time-zones                        # all unit tests, once per time zone
npx tsc -p tsconfig.spec.json --noEmit         # type-checks the specs too
npx prettier --check --end-of-line auto src    # formatting
npm run i18n:check                             # every text has its Turkish version
```

- **`test:time-zones`** runs the whole suite in six zones: Istanbul, New York, Berlin, Tokyo,
  Sydney and Kathmandu (UTC+05:45). Date code that passes in one can fail in another around a
  clock change or a half-hour offset. `npm run test:time-zones -- Asia/Kolkata` runs just the
  zones given. Setting `TZ=...` in front of `ng test` doesn't work in Git Bash on Windows; the
  script sets it for each run itself. CI runs it on every pull request.
- **`tsc -p tsconfig.spec.json`:** `ng test` compiles specs without checking their types, so a
  spec that builds a model with a missing field still runs. CI doesn't run this one, so run it
  yourself.
- **`--end-of-line auto`:** Windows checkouts have CRLF line endings, and Prettier would
  otherwise flag every file.

## Screens

| Route | Who | What |
|---|---|---|
| `/` | visitors | Front page; signed-in users go to the dashboard |
| `/login`, `/forgot-password`, `/reset-password` | anyone | Sign-in, and a password reset by emailed link |
| `/register` | visitors | Sign-up: a team member gives their manager's email and waits for that manager; a manager waits for an admin |
| `/change-password` | signed in | Change your password. Forced, before anything else, after signing in with a temporary password |
| `/dashboard` | signed in | Today's reservations, what's in use now, your next bookings |
| `/resources` | signed in | Your team's resources and the shared ones; admins see every resource. Each row says whose team it belongs to, or "Shared" |
| `/resources/new`, `/resources/:id/edit` | Admin, Manager | Admins create and edit any resource and pick the teams that may use it (none: shared by everyone). A manager adds resources to their own team, and edits, activates and deactivates only the ones that belong to their team alone (ADR-0022) |
| `/availability` | signed in | What's free for a time range, by type and capacity |
| `/calendar` | signed in | The shared office calendar: who booked what |
| `/reservations/new`, `/reservations/:id/edit` | signed in | Book, change or cancel. Read-only for anyone but the owner or an admin |
| `/reservations/mine` | signed in | Your upcoming, past and cancelled reservations |
| `/admin` | Admin | Resource counts, and every reservation in a date range to change or cancel |
| `/admin/reports` | Admin | Usage report |
| `/admin/blocks` | Admin | Blocked windows: take a resource out of booking for a while |
| `/admin/users` | Admin | Every account: approve or reject sign-ups, add accounts with a temporary password, change roles and teams, set temporary passwords, disable and enable |
| `/team` | Manager | Your team: approve or reject requests to join, add members with a temporary password, set temporary passwords, disable, enable, remove |

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

## Browser tests

`e2e/` holds [Playwright](https://playwright.dev/) tests that drive the real app in a browser:
sign-in and the language, booking with a conflict, a booking reaching another window's calendar
live, a blocked window, and a manager's team resource. `screens.spec.ts` also opens every
screen in Turkish, runs [axe](https://github.com/dequelabs/axe-core) on it (WCAG 2.1 AA and best
practices), and checks that nothing is wider than a 390px phone. They need the whole app
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
- **Data:** each test makes its own resource through the API. At the end it cancels that
  resource's bookings and deactivates it, since resources can't be deleted. A local database
  collects inactive "E2E …" resources over time; they don't affect anything else.
- **A failure** keeps a trace in `test-results/`: `npx playwright show-trace <file>` replays it
  step by step.
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
