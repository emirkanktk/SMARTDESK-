## Summary

<!-- What does this PR change, and why? -->

## Area / owner

<!-- Which module does this touch (backend / frontend / integration)? Request the author's designated reviewer from docs/TEAM_SYNC.md. If this changes another owner's code or a shared contract (API_CONTRACTS, DATA_MODEL, SignalR event shapes), request that owner too - their approval is required. -->

## Test plan

- [ ] `dotnet build` - 0 errors
- [ ] `dotnet test` passes
- [ ] `ng build` / `ng test` pass (if `frontend/` changed)
- [ ] Manually checked: <!-- endpoint called, screen opened, etc. -->

## Checklist

- [ ] Approved by the author's designated reviewer (or by the third teammate after one working day), plus the owner of any other area or shared contract this changes (see docs/TEAM_SYNC.md)
- [ ] `docs/API_CONTRACTS.md` / `docs/DATA_MODEL.md` updated if an endpoint, business rule, or the data model changed
- [ ] New EF Core migration included if the model changed
- [ ] No secrets, connection strings with real credentials, or API keys committed
