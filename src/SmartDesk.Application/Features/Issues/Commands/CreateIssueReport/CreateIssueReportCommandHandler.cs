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
        // At the door, nobody needs to be signed in (ADR-0050): the code is what lets them in.
        var userId = currentUser.UserId;
        if (userId is null && request.DoorCode is null)
        {
            throw new UnauthorizedException();
        }

        var id = Guid.CreateVersion7();

        // Under the resource's lock (ADR-0011), so two people reporting the same problem at the
        // same moment go one after the other, and the second is told it's already reported.
        await using (var transaction = await db.BeginTransactionAsync(cancellationToken))
        {
            await db.LockResourceForBookingAsync(request.ResourceId, cancellationToken);

            var resourceName = await ResourceNameAsync(request, cancellationToken);

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

    // Another team's resource isn't there for the caller (ADR-0022). At the door it is, for
    // whoever has its code; a wrong code is the same 404 as no resource, so nothing is given away.
    private async Task<string> ResourceNameAsync(CreateIssueReportCommand request, CancellationToken cancellationToken)
    {
        if (request.DoorCode is null)
        {
            return await db.Resources
                .UsableBy(currentUser)
                .Where(r => r.Id == request.ResourceId)
                .Select(r => r.Name)
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new NotFoundException(nameof(Resource), request.ResourceId);
        }

        var resource = await db.Resources
            .Where(r => r.Id == request.ResourceId)
            .Select(r => new { r.Name, r.CheckInCode, r.DoorScreenTokenHash })
            .SingleOrDefaultAsync(cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return resource is not null
            && CheckInRules.SeenAtTheDoor(resource.CheckInCode, resource.DoorScreenTokenHash, request.DoorCode, now)
            ? resource.Name
            : throw new NotFoundException(nameof(Resource), request.ResourceId);
    }
}
