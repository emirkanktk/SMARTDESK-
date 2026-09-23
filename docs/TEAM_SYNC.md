# Team Sync

## Roster and ownership

| Person | Focus | Owns |
|---|---|---|
| Emirkan | Backend | Onion solution structure, Domain/Application layers, CQRS+MediatR pipeline, EF Core/PostgreSQL migrations, JWT/Identity auth, reservation business rules |
| Lütfi | Frontend | Angular architecture, screens, forms, API integration |
| Burak | Integration | Auth support, SignalR, Docker, optional LLM integration, integration tests |

Two-person fallback: one person leans backend, one leans frontend; authentication and
SignalR are handled together since both sides need to agree on the contract.

Don't change another owner's area without coordinating first - open a PR and tag them, or
raise it in whatever channel the team actually uses day to day.

## Branching

- One independent piece of work = one branch, named `feature/<short-description>` (e.g.
  `feature/backend-core`, `feature/reservation-cqrs`) or `fix/<short-description>` /
  `docs/<short-description>` for non-feature work.
- Stack a branch on whichever other open branch it actually depends on (e.g. a feature
  that needs `feature/backend-core`'s DbContext branches from that branch, not from `main`,
  until it merges) rather than redoing work once the dependency lands.
- PRs are merged only after review. Since the team is small, any of the three of us can
  review any PR, but the module owner (see the table above) should always be one of the
  reviewers for changes to their area.

## Claiming shared or cross-cutting work

Before starting on something nobody's picked up yet - especially something that touches
more than one person's area (auth, the reservation overlap contract, SignalR event
shapes) - say so first (a PR description, a comment, whatever's visible to the other two)
so two people don't build the same thing in parallel.

## Keeping docs honest

`docs/API_CONTRACTS.md` and `docs/DATA_MODEL.md` describe what the system *is meant to*
look like from the outside. If a PR changes an endpoint's shape, a business rule, or the
data model, update the relevant doc in the same PR - a contract nobody kept in sync with
the code is worse than no contract.
