using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Issues.Common;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.Issues.Queries.GetIssuePhoto;

// The photo of a reported problem (GET /api/issues/{id}/photo, ADR-0037), for anyone who may use
// its resource, like the resource's own photo (ADR-0027). A report never changes, so neither
// does its photo.
public sealed record GetIssuePhotoQuery(Guid IssueId) : IRequest<IssuePhotoFile>;

public sealed record IssuePhotoFile(string ContentType, byte[] Data);

public sealed class GetIssuePhotoQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetIssuePhotoQuery, IssuePhotoFile>
{
    public async Task<IssuePhotoFile> Handle(GetIssuePhotoQuery request, CancellationToken cancellationToken)
    {
        if (!await db.IssueReports.OnUsableResources(currentUser).AnyAsync(i => i.Id == request.IssueId, cancellationToken))
        {
            throw new NotFoundException(nameof(IssueReport), request.IssueId);
        }

        return await db.IssueReportPhotos
            .AsNoTracking()
            .Where(p => p.IssueReportId == request.IssueId)
            .Select(p => new IssuePhotoFile(p.ContentType, p.Data))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(IssueReportPhoto), request.IssueId);
    }
}
