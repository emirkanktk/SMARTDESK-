using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Issues.Common;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.Issues.Commands.CreateIssueReport;

public sealed class CreateIssueReportCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IssueReader reader,
    IIssueNotifier notifier,
    TimeProvider timeProvider) : IRequestHandler<CreateIssueReportCommand, IssueReportDto>
{
    public async Task<IssueReportDto> Handle(CreateIssueReportCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var id = Guid.CreateVersion7();

        // Under the resource's lock (ADR-0011), so two people reporting the same problem at the
        // same moment go one after the other, and the second is told it's already reported.
        await using (var transaction = await db.BeginTransactionAsync(cancellationToken))
        {
            await db.LockResourceForBookingAsync(request.ResourceId, cancellationToken);

            // Another team's resource isn't there for the caller (ADR-0022).
            var resourceName = await db.Resources
                .UsableBy(currentUser)
                .Where(r => r.Id == request.ResourceId)
                .Select(r => r.Name)
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new NotFoundException(nameof(Resource), request.ResourceId);

            var category = request.Category!.Value;
            if (IssueRules.CanBeDuplicate(category)
                && await db.IssueReports.AnyAsync(IssueRules.SameProblem(request.ResourceId, category), cancellationToken))
            {
                throw new ConflictException(string.Format(Messages.Issues_AlreadyReported, resourceName))
                {
                    Code = ConflictCodes.IssueAlreadyReported,
                };
            }

            var issue = new IssueReport
            {
                Id = id,
                ResourceId = request.ResourceId,
                ReportedByUserId = userId,
                Category = category,
                Description = request.Description.Trim(),
                IsBlocking = request.IsBlocking,
                Status = IssueStatus.Open,
                CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
            };
            if (request.Photo is { Length: > 0 } photo)
            {
                issue.Photo = new IssueReportPhoto
                {
                    IssueReportId = id,
                    ContentType = ResourcePhotoRules.DetectContentType(photo)!,
                    Data = photo,
                };
            }

            db.IssueReports.Add(issue);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var dto = await reader.GetAsync(db.IssueReports.AsNoTracking().Where(i => i.Id == id), cancellationToken);
        await notifier.IssueReportedAsync(dto, cancellationToken);
        return dto;
    }
}
