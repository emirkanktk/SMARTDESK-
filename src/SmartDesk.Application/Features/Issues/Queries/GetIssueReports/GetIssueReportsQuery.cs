using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Issues.Common;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Issues.Queries.GetIssueReports;

// The reports on the resources the caller may manage (GET /api/issues, ADR-0037): every one for
// an Admin, those on their team's own resources for a Manager. Unresolved first, and among them
// the ones that make a resource unusable; newest first after that.
public sealed record GetIssueReportsQuery(IssueStatus? Status, Guid? ResourceId) : IRequest<IReadOnlyList<IssueReportDto>>;

public sealed class GetIssueReportsQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser, IssueReader reader)
    : IRequestHandler<GetIssueReportsQuery, IReadOnlyList<IssueReportDto>>
{
    public Task<IReadOnlyList<IssueReportDto>> Handle(GetIssueReportsQuery request, CancellationToken cancellationToken)
    {
        var issues = db.IssueReports.AsNoTracking().OnManageableResources(currentUser);
        if (request.Status is { } status)
        {
            issues = issues.Where(i => i.Status == status);
        }

        if (request.ResourceId is { } resourceId)
        {
            issues = issues.Where(i => i.ResourceId == resourceId);
        }

        return reader.ListAsync(
            issues
                .OrderBy(i => i.Status == IssueStatus.Resolved)
                .ThenByDescending(i => i.IsBlocking)
                .ThenByDescending(i => i.CreatedAt),
            cancellationToken);
    }
}
