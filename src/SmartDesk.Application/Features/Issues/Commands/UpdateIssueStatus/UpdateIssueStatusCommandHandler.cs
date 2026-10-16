using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Issues.Common;
using SmartDesk.Application.Features.Meetings.Common;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Issues.Commands.UpdateIssueStatus;

public sealed class UpdateIssueStatusCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IssueReader reader,
    IIssueNotifier notifier,
    MeetingInvitations inbox,
    TimeProvider timeProvider) : IRequestHandler<UpdateIssueStatusCommand, IssueReportDto>
{
    public async Task<IssueReportDto> Handle(UpdateIssueStatusCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();

        // A report on another team's resource isn't there for the caller (ADR-0022).
        var resourceId = await db.IssueReports
            .OnUsableResources(currentUser)
            .Where(i => i.Id == request.Id)
            .Select(i => (Guid?)i.ResourceId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(IssueReport), request.Id);

        bool changed;
        IReadOnlyList<InboxMessage> messages = [];

        // The resource's lock keeps two people from moving the same report at once: whoever
        // comes second sees what the first one did, so it's resolved, and its reporter told,
        // only once.
        await using (var transaction = await db.BeginTransactionAsync(cancellationToken))
        {
            await db.LockResourceForBookingAsync(resourceId, cancellationToken);

            var issue = await db.IssueReports
                .Include(i => i.Resource).ThenInclude(r => r.Teams)
                .SingleAsync(i => i.Id == request.Id, cancellationToken);
            if (!IssueAccess.CanManage(issue, currentUser))
            {
                throw new ForbiddenAccessException(ResourceManagement.NotYoursMessage);
            }

            // Resolving it again changes nothing; reopening it isn't possible: the problem
            // coming back is a new report.
            if (issue.Status == IssueStatus.Resolved && request.Status != IssueStatus.Resolved)
            {
                throw new ConflictException(Messages.Issues_AlreadyResolved)
                {
                    Code = ConflictCodes.IssueResolved,
                };
            }

            changed = issue.Status != request.Status;
            if (changed)
            {
                issue.Status = request.Status;
                if (request.Status == IssueStatus.Resolved)
                {
                    issue.ResolvedAt = timeProvider.GetUtcNow().UtcDateTime;
                    messages = TellReporter(issue, userId);
                }

                await db.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        var dto = await reader.GetAsync(db.IssueReports.AsNoTracking().Where(i => i.Id == request.Id), cancellationToken);
        if (changed)
        {
            await notifier.IssueUpdatedAsync(dto, cancellationToken);
            await inbox.SendAsync(messages, cancellationToken);
        }

        return dto;
    }

    // Whoever reported it hears that it's fixed, unless they fixed it themselves, or reported it
    // at the door without an account (ADR-0050).
    private IReadOnlyList<InboxMessage> TellReporter(IssueReport issue, Guid resolvedBy)
    {
        if (issue.ReportedByUserId is not { } reporter || reporter == resolvedBy)
        {
            return [];
        }

        var message = new InboxMessage
        {
            Id = Guid.CreateVersion7(),
            RecipientId = reporter,
            Kind = InboxMessageKind.IssueResolved,
            SenderId = resolvedBy,
            ResourceName = issue.Resource.Name,
            StartDateTime = issue.CreatedAt,
            EndDateTime = issue.ResolvedAt!.Value,
            Note = issue.Description,
            IssueCategory = issue.Category,
            CreatedAt = issue.ResolvedAt.Value,
        };
        db.InboxMessages.Add(message);
        return [message];
    }
}
