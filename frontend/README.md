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
