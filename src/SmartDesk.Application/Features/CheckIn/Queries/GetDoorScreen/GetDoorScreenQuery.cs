using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Extensions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.CheckIn.Common;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.CheckIn.Queries.GetDoorScreen;

// What a resource's door screen shows (ADR-0034), asked by the screen itself with its own token
// rather than a user's sign-in: the room, today's bookings as times (a corridor is no place for
// names), its blocked windows, and, when it asks for a check-in, the check-in link with this
// step's changing code. A wrong token is the same 404 as no resource, so nothing is given away.
public sealed record GetDoorScreenQuery(Guid ResourceId, string? Token, string? TimeZone = null) : IRequest<DoorScreenDto>;

public sealed record DoorScreenDto(
    string ResourceName,
    string Location,
    int Capacity,
    ResourceStatus Status,
    DateTime Now,
    IReadOnlyList<DoorBookingDto> Bookings,
    IReadOnlyList<DoorBlockDto> Blocks,
    DoorCheckInDto? CheckIn);

// A booking of today that isn't over, earliest first. CheckInNeeded: it has to be checked in
// at the door; CheckedIn: someone has.
public sealed record DoorBookingDto(DateTime StartDateTime, DateTime EndDateTime, bool CheckInNeeded, bool CheckedIn)
{
    // A booking just checked in at the door, as the page there sees it without an account
    // (ADR-0050): its times, and nobody's name.
    public static DoorBookingDto CheckedInAtTheDoor(ReservationDto reservation) =>
        new(reservation.StartDateTime, reservation.EndDateTime, CheckInNeeded: true, CheckedIn: reservation.CheckedInAt is not null);
}

public sealed record DoorBlockDto(DateTime StartDateTime, DateTime EndDateTime, string Reason);

// The link to put in the QR code until ChangesAt, when the screen asks again.
public sealed record DoorCheckInDto(string Url, DateTime ChangesAt);

public sealed class GetDoorScreenQueryValidator : AbstractValidator<GetDoorScreenQuery>
{
    public GetDoorScreenQueryValidator()
    {
        RuleFor(x => x.TimeZone)
            .Must(id => TimeZoneInfo.TryFindSystemTimeZoneById(id!, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.TimeZone))
            .WithMessage(_ => Messages.Validation_UnknownTimeZone);
    }
}

public sealed class GetDoorScreenQueryHandler(IApplicationDbContext db, IFrontendLinks links, TimeProvider timeProvider)
    : IRequestHandler<GetDoorScreenQuery, DoorScreenDto>
{
    // A screen shows a morning's or an afternoon's worth; more would not fit anyway.
    public const int MaxBookings = 8;

    public async Task<DoorScreenDto> Handle(GetDoorScreenQuery request, CancellationToken cancellationToken)
    {
        var resource = await db.Resources
            .AsNoTracking()
            .Where(r => r.Id == request.ResourceId)
            .Select(r => new { r.Name, r.Location, r.Capacity, r.Status, r.CheckInRequired, r.DoorScreenTokenHash })
            .SingleOrDefaultAsync(cancellationToken);
        if (resource is null || !CheckInRules.DoorScreenTokenMatches(resource.DoorScreenTokenHash, request.Token))
        {
            throw new NotFoundException(nameof(Resource), request.ResourceId);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var zone = string.IsNullOrWhiteSpace(request.TimeZone)
            ? TimeZoneInfo.Utc
            : TimeZoneInfo.FindSystemTimeZoneById(request.TimeZone);
        var (_, dayEnd) = zone.LocalDayBoundsUtc(now);
        var (bookings, blocks) = await DoorDay.LoadAsync(db, request.ResourceId, now, dayEnd, MaxBookings, cancellationToken);

        var checkIn = resource.CheckInRequired
            ? new DoorCheckInDto(
                links.CheckIn(request.ResourceId, CheckInRules.ChangingCodeAt(resource.DoorScreenTokenHash!, now)),
                CheckInRules.StepEndsAt(now))
            : null;

        return new DoorScreenDto(
            resource.Name, resource.Location, resource.Capacity, resource.Status, now, bookings, blocks, checkIn);
    }
}
