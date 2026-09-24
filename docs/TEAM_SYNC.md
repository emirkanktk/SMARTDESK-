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
raise it in whatever channel the team actually uses day to day. Tagging the module owner
is a notice; it adds a required approval only in the cases listed under *Review and merge
rules* (another owner's code or a shared contract).

## Branching

- One independent piece of work = one branch, named `feature/<short-description>` (e.g.
  `feature/backend-core`, `feature/reservation-cqrs`) or `fix/<short-description>` /
  `docs/<short-description>` for non-feature work.
- Stack a branch on whichever other open branch it actually depends on (e.g. a feature
  that needs `feature/backend-core`'s DbContext branches from that branch, not from `main`,
  until it merges) rather than redoing work once the dependency lands.

## Review and merge rules

Each author has exactly one reviewer responsible for approving their PRs. That approval is
required before merge, including one-line fixes and docs-only changes. The other two may
comment, but their approval is not required. The author does not approve their own PR.
If the designated reviewer is away or hasn't reviewed within one working day, the third
teammate may approve instead - say so in the PR.

| Author | Responsible reviewer |
|---|---|
| Emirkan | Burak |
| Lütfi | Emirkan |
| Burak | Lütfi |

Request that reviewer on every PR. Approval follows the author, not the module, with one
exception: a PR that changes another owner's code or a shared contract
(`docs/API_CONTRACTS.md`, `docs/DATA_MODEL.md`, SignalR event shapes) also needs that
owner's approval. Request them as a reviewer too.

GitHub does not enforce this for us: branch protection and rulesets aren't available on a
private repo under a free plan, so the merge button works even on an unapproved PR. The
rule holds only because we follow it - only a reviewer who approved clicks merge, and
only once every approval the PR needs is in.

- Nobody pushes directly to `main`. Every change reaches `main` through a PR.
- Answer or resolve every review comment before merging. If you push substantial changes
  after getting an approval, re-request review from the designated reviewer instead of
  merging on the old approval.
- **The reviewer who approved the PR merges it - not the author.** Before merging, check
  that nothing was pushed after your approval (if something was, review it first) and that
  the branch still builds and passes tests. Use a merge commit, not squash, so stacked PRs
  built on the branch keep a clean diff.
  - When a PR needs two approvals (designated reviewer plus an owner), the designated
    reviewer merges once both are in.
  - When the third teammate approved under the one-working-day fallback, they merge.
- Don't merge while a requested reviewer is still reviewing - wait for their review or ask
  them first, even if you already have the approval you need.
- After merging a PR that another open PR is stacked on, retarget that PR to `main`
  (`gh pr edit <n> --base main`). GitHub only does it automatically when the merged branch
  is deleted.
- Use the checklist in `.github/pull_request_template.md` - it's filled in automatically
  when you open a PR on GitHub.

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
