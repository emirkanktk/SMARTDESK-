## Summary

<!-- What does this PR change, and why? -->

## Area / owner

<!-- Which module does this touch (backend / frontend / integration)? Request the author's designated reviewer from docs/TEAM_SYNC.md. Tagging a module owner is a notice, not a second required approval. -->

## Test plan

- [ ] `dotnet build` - 0 errors
- [ ] `dotnet test` passes
- [ ] `ng build` / `ng test` pass (if `frontend/` changed)
- [ ] Manually checked: <!-- endpoint called, screen opened, etc. -->

## Checklist

- [ ] Approved by the author's designated reviewer, or by the third teammate after one working day (see docs/TEAM_SYNC.md)
- [ ] `docs/API_CONTRACTS.md` / `docs/DATA_MODEL.md` updated if an endpoint, business rule, or the data model changed
- [ ] New EF Core migration included if the model changed
- [ ] No secrets, connection strings with real credentials, or API keys committed
