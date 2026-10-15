using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Calendar.Common;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.Calendar.Queries.GetReservationCalendar;

// One booking to add to a calendar app, as an .ics file (ADR-0048): for its owner or someone
// invited to it, since it goes in their calendar. Anyone else gets the same 404 as for no
// booking. A cancelled booking comes as cancelled, so a calendar that has it can mark it so.
public sealed record GetReservationCalendarQuery(Guid Id) : IRequest<CalendarDocument>;

public sealed class GetReservationCalendarQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IIdentityService identityService,
    IFrontendLinks links,
    ICheckInTimes checkInTimes,
    TimeProvider timeProvider) : IRequestHandler<GetReservationCalendarQuery, CalendarDocument>
{
    public async Task<CalendarDocument> Handle(GetReservationCalendarQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();

        var reservation = await db.Reservations
            .AsNoTracking()
            .Where(r => r.Id == request.Id && (r.UserId == userId || r.Attendees.Any(a => a.UserId == userId)))
            .Select(ReservationDto.Projection)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(Reservation), request.Id);
        reservation = await identityService.WithUserNameAsync(reservation, cancellationToken);

        return new CalendarDocument(
            Messages.Calendar_Name,
            timeProvider.GetUtcNow().UtcDateTime,
            [CalendarEntries.For(reservation, userId, links, checkInTimes.Grace)]);
    }
}
