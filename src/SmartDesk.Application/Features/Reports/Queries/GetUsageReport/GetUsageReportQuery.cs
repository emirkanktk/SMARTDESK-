using MediatR;

namespace SmartDesk.Application.Features.Reports.Queries.GetUsageReport;

// Admin only (enforced on the endpoint). From and To are whole local days, both included, in
// TimeZone (an IANA id; UTC when omitted) - the same way the dashboard reads "today".
public sealed record GetUsageReportQuery(
    DateOnly From,
    DateOnly To,
    string? TimeZone = null,
    Guid? ResourceTypeId = null) : IRequest<UsageReportDto>;
