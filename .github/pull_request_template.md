## Summary

<!-- What does this PR change, and why? -->

## Area / owner

<!-- Which module does this touch (backend / frontend / integration)? Tag the module owner as a reviewer - see docs/TEAM_SYNC.md. -->

## Test plan

- [ ] `dotnet build` - 0 errors
- [ ] `dotnet test` passes
- [ ] `ng build` / `ng test` pass (if `frontend/` changed)
- [ ] Manually checked: <!-- endpoint called, screen opened, etc. -->

## Checklist

- [ ] At least one approving review from someone other than the author before merging
- [ ] Module owner is among the reviewers for changes to their area
- [ ] `docs/API_CONTRACTS.md` / `docs/DATA_MODEL.md` updated if an endpoint, business rule, or the data model changed
- [ ] New EF Core migration included if the model changed
- [ ] No secrets, connection strings with real credentials, or API keys committed
