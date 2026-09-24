using MediatR;

namespace SmartDesk.Application.Features.Dashboard.Queries.GetDashboard;

// TimeZone is an IANA id ("Europe/Istanbul") deciding what "today" means; the browser knows
// it (Intl.DateTimeFormat().resolvedOptions().timeZone). Defaults to UTC when omitted.
public sealed record GetDashboardQuery(string? TimeZone = null) : IRequest<DashboardDto>;
