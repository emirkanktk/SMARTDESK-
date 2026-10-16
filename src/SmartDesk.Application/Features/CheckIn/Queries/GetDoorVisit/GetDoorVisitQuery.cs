using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Extensions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.CheckIn.Common;
using SmartDesk.Application.Features.CheckIn.Queries.GetDoorScreen;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.CheckIn.Queries.GetDoorVisit;

// The page a resource's QR code opens, for whoever has no account (ADR-0050). It asks with the
// code it was opened with, in a header, rather than a user's sign-in: the printed code, or one of
// the door screen's of the last half hour. It gets what the door screen shows - the room, the rest
// of today's bookings as times, its blocked windows - and what the room has, and the problems
// reported there and not fixed yet, without who reported them. A wrong code is the same 404 as
// no resource, so nothing is given away.
public sealed record GetDoorVisitQuery(Guid ResourceId, string? Code, string? TimeZone = null) : IRequest<DoorVisitDto>;

public sealed record DoorVisitDto(
    string ResourceName,
    string ResourceTypeName,
    string Location,
    int Capacity,
    string? Description,
    IReadOnlyList<string> Features,
    ResourceStatus Status,
    bool CheckInRequired,
    DateTime Now,
    IReadOnlyList<DoorBookingDto> Bookings,
    IReadOnlyList<DoorBlockDto> Blocks,
    IReadOnlyList<DoorIssueDto> OpenIssues);

// A problem reported there and not fixed yet, which the page shows before anyone reports it
// again. Nobody's name, and no photo.
public sealed record DoorIssueDto(
    Guid Id,
    IssueCategory Category,
    string Description,
    bool IsBlocking,
    IssueStatus Status,
    DateTime CreatedAt);

public sealed class GetDoorVisitQueryValidator : AbstractValidator<GetDoorVisitQuery>
{
    public GetDoorVisitQueryValidator()
    {
        RuleFor(x => x.TimeZone)
            .Must(id => TimeZoneInfo.TryFindSystemTimeZoneById(id!, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.TimeZone))
            .WithMessage(_ => Messages.Validation_UnknownTimeZone);
    }
}

public sealed class GetDoorVisitQueryHandler(IApplicationDbContext db, TimeProvider timeProvider)
    : IRequestHandler<GetDoorVisitQuery, DoorVisitDto>
{
    // The rest of a working day, on a phone that scrolls.
    public const int MaxBookings = 20;

    public async Task<DoorVisitDto> Handle(GetDoorVisitQuery request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var resource = await db.Resources
            .AsNoTracking()
            .Where(r => r.Id == request.ResourceId)
            .Select(r => new
            {
                r.Name,
                TypeName = r.ResourceType.Name,
                r.Location,
                r.Capacity,
                r.Description,
                Features = r.Features.OrderBy(f => f.Name).Select(f => f.Name).ToList(),
                r.Status,
                r.CheckInRequired,
                r.CheckInCode,
                r.DoorScreenTokenHash,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (resource is null
            || !CheckInRules.SeenAtTheDoor(resource.CheckInCode, resource.DoorScreenTokenHash, request.Code, now))
        {
            throw new NotFoundException(nameof(Resource), request.ResourceId);
        }

        var zone = string.IsNullOrWhiteSpace(request.TimeZone)
            ? TimeZoneInfo.Utc
            : TimeZoneInfo.FindSystemTimeZoneById(request.TimeZone);
        var (_, dayEnd) = zone.LocalDayBoundsUtc(now);
        var (bookings, blocks) = await DoorDay.LoadAsync(db, request.ResourceId, now, dayEnd, MaxBookings, cancellationToken);

        // As the signed-in page lists them (ADR-0037): those that make it unusable first, then the newest.
        var issues = await db.IssueReports
            .AsNoTracking()
            .Where(i => i.ResourceId == request.ResourceId)
            .Where(IssueRules.Unresolved)
            .OrderByDescending(i => i.IsBlocking)
            .ThenByDescending(i => i.CreatedAt)
            .Select(i => new DoorIssueDto(i.Id, i.Category, i.Description, i.IsBlocking, i.Status, i.CreatedAt))
            .ToListAsync(cancellationToken);

        return new DoorVisitDto(
            resource.Name,
            resource.TypeName,
            resource.Location,
            resource.Capacity,
            resource.Description,
            resource.Features,
            resource.Status,
            resource.CheckInRequired,
            now,
            bookings,
            blocks,
            issues);
    }
}
