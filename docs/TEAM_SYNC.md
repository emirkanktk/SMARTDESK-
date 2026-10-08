# Team Sync

## Roster and ownership

| Person | Focus | Owns |
|---|---|---|
| Lütfi | Everything, since 2026-10-08 | Backend (Onion + CQRS, EF Core/PostgreSQL, Identity, SignalR), frontend (Angular), Docker, CI and the docs |

The team started with three people. Burak (integration) left on 2026-09-28 and his area
moved to Emirkan: SignalR (#17), Docker (#18) and the integration tests (#21). Emirkan
(backend and integration) left on 2026-10-08. Until then he owned the solution structure,
the Domain and Application layers, the migrations, authentication and the reservation rules,
and Lütfi owned the frontend. Lütfi also built the optional LLM assistant (ADR-0029) and the
app's own container (ADR-0031). Since 2026-10-08 one developer owns all of it, and the
issues the others left open are theirs too (e.g. #101).

## Branching

- One independent piece of work = one branch, named `feature/<short-description>`,
  `fix/<short-description>` or `docs/<short-description>`.
- Stack a branch on whichever other unmerged branch it actually depends on, rather than
  redoing work once the dependency lands.

## Merging

With one developer there is nobody to review a pull request, so a PR is optional. The
two-person review rule that applied until 2026-10-08 is retired. What still holds:

- `main` changes only by merging a branch, with a merge commit rather than a squash, so the
  history keeps each piece of work together.
- Before merging, the branch passes the same checks CI runs (`.github/workflows/ci.yml`):
  the backend build without warnings, unit and integration tests, the frontend build, unit
  tests in several time zones, formatting, the Turkish texts and the browser tests.
- CI also runs on every push to `main`. A red run there is fixed before anything else.
- A PR is still the way to go when a change needs its CI run before it reaches `main`, or a
  write-up for later. Use the checklist in `.github/pull_request_template.md`.

## Keeping docs honest

`docs/API_CONTRACTS.md` and `docs/DATA_MODEL.md` describe what the system *is meant to*
look like from the outside. If a change touches an endpoint's shape, a business rule, or the
data model, update the relevant doc in the same branch - a contract nobody kept in sync with
the code is worse than no contract.
