using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Issues.Common;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.Issues.Queries.GetOpenIssues;

// The problems still open on a resource (GET /api/resources/{id}/issues, ADR-0037), for anyone
// who may use it: the page its QR code opens warns about them, so nobody reports one twice.
// Those that make it unusable first, then the newest.
public sealed record GetOpenIssuesQuery(Guid ResourceId) : IRequest<IReadOnlyList<IssueReportDto>>;

public sealed class GetOpenIssuesQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser, IssueReader reader)
    : IRequestHandler<GetOpenIssuesQuery, IReadOnlyList<IssueReportDto>>
{
    public async Task<IReadOnlyList<IssueReportDto>> Handle(GetOpenIssuesQuery request, CancellationToken cancellationToken)
    {
        if (!await db.Resources.UsableBy(currentUser).AnyAsync(r => r.Id == request.ResourceId, cancellationToken))
        {
            throw new NotFoundException(nameof(Resource), request.ResourceId);
        }

        return await reader.ListAsync(
            db.IssueReports
                .AsNoTracking()
                .Where(i => i.ResourceId == request.ResourceId)
                .Where(IssueRules.Unresolved)
                .OrderByDescending(i => i.IsBlocking)
                .ThenByDescending(i => i.CreatedAt),
            cancellationToken);
    }
}
