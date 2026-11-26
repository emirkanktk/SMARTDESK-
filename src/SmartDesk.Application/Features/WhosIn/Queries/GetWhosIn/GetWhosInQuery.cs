using System.Globalization;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Extensions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.WhosIn.Queries.GetWhosIn;

// Who's in the office on a day (GET /api/whos-in, ADR-0063): everyone with a booking that day,
// and everyone who said they'd come to a meeting booked then, each with where and when. The day
// is a local one in TimeZone, the browser's ("Europe/Istanbul"), or UTC's when it's left out;
// Date is today there when it's left out.
public sealed record GetWhosInQuery(DateOnly? Date = null, string? TimeZone = null) : IRequest<WhosInDto>;

public sealed class GetWhosInQueryValidator : AbstractValidator<GetWhosInQuery>
{
    public GetWhosInQueryValidator()
    {
        RuleFor(x => x.TimeZone)
            .Must(id => TimeZoneInfo.TryFindSystemTimeZoneById(id!, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.TimeZone))
            .WithMessage(_ => Messages.Validation_UnknownTimeZone);

        // Wide enough for any plan, and far from the ends of the calendar, where a day in a zone
        // ahead of UTC or behind it has no instant to begin or end at.
        RuleFor(x => x.Date)
            .Must(date => date is null || date.Value.Year is >= 2000 and <= 2100)
            .WithMessage(_ => Messages.Validation_DayOutOfRange);
    }
}

public sealed class GetWhosInQueryHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IIdentityService identityService,
    IUserAccounts accounts,
    TimeProvider timeProvider) : IRequestHandler<GetWhosInQuery, WhosInDto>
{
    public async Task<WhosInDto> Handle(GetWhosInQuery request, CancellationToken cancellationToken)
    {
        var me = currentUser.UserId ?? throw new UnauthorizedException();
        var zone = string.IsNullOrWhiteSpace(request.TimeZone)
            ? TimeZoneInfo.Utc
            : TimeZoneInfo.FindSystemTimeZoneById(request.TimeZone);
        var date = request.Date
            ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(timeProvider.GetUtcNow().UtcDateTime, zone));
        var dayStart = zone.LocalToUtc(date.ToDateTime(TimeOnly.MinValue));
        var dayEnd = zone.LocalToUtc(date.AddDays(1).ToDateTime(TimeOnly.MinValue));

        // The bookings that bring people in that day: confirmed ones - not cancelled or released,
        // nor still waiting for an Admin's approval (ADR-0049) - on what the caller may use
        // (ADR-0022), as on the dashboard, which already names who booked each.
        var bookings = await db.Reservations
            .AsNoTracking()
            .OnUsableResources(currentUser)
            .Where(r => r.Status == ReservationStatus.Confirmed && r.StartDateTime < dayEnd && r.EndDateTime > dayStart)
            .Select(r => new Booking(
                r.Id,
                r.UserId,
                r.ResourceId,
                r.Resource.Name,
                r.Resource.ResourceType.Name,
                r.Resource.Location,
                r.StartDateTime,
                r.EndDateTime,
                r.CheckedInAt != null,
                r.Attendees.Where(a => a.Response == AttendeeResponse.Accepted).Select(a => a.UserId).ToList()))
            .ToListAsync(cancellationToken);

        // Each booking brings its owner, and whoever said they'd come; the organizer's name goes
        // with the meeting.
        var visits = bookings
            .SelectMany(b => b.Coming
                .Where(id => id != b.OwnerId)
                .Select(id => new Visit(id, b, Invited: true))
                .Prepend(new Visit(b.OwnerId, b, Invited: false)))
            .ToList();
        var names = await identityService.GetDisplayNamesAsync(
            visits.Select(v => v.PersonId).Concat(bookings.Select(b => b.OwnerId)).Distinct().ToList(),
            cancellationToken);
        var team = await TeamOfCallerAsync(cancellationToken);

        // Names in the order of the request's language (ADR-0023), whose alphabet it is: in Turkish
        // "Işıl" comes before "İpek". Only this list's order: the code's own formatting and
        // comparing stays in English's culture.
        var byName = StringComparer.Create(CultureInfo.CurrentUICulture, ignoreCase: true);
        var people = visits
            // Someone whose account is gone isn't anyone to meet.
            .Where(v => names.ContainsKey(v.PersonId))
            .GroupBy(v => v.PersonId)
            .Select(person => new OfficePersonDto(
                person.Key,
                names[person.Key],
                person.Key == me,
                team.Contains(person.Key),
                person
                    .OrderBy(v => v.Booking.Start)
                    .ThenBy(v => v.Booking.End)
                    .Select(v => new OfficeVisitDto(
                        v.Booking.Id,
                        v.Booking.ResourceId,
                        v.Booking.ResourceName,
                        v.Booking.ResourceTypeName,
                        v.Booking.Location,
                        v.Booking.Start,
                        v.Booking.End,
                        v.Booking.CheckedIn,
                        v.Invited,
                        v.Invited ? names.GetValueOrDefault(v.Booking.OwnerId) : null))
                    .ToList()))
            .OrderBy(p => p.Name, byName)
            .ToList();

        return new WhosInDto(date, zone.Id, currentUser.TeamId is not null, people);
    }

    // The caller's team (ADR-0020): its Manager and the Employees in it who can sign in. Nobody
    // for an Admin, or an Employee outside any team.
    private async Task<IReadOnlySet<Guid>> TeamOfCallerAsync(CancellationToken cancellationToken)
    {
        if (currentUser.TeamId is not { } team)
        {
            return new HashSet<Guid>();
        }

        var members = await accounts.ListAsync(new UserFilter(Status: AccountStatus.Active, ManagerId: team), cancellationToken);
        return members.Select(m => m.Id).Append(team).ToHashSet();
    }

    private sealed record Booking(
        Guid Id,
        Guid OwnerId,
        Guid ResourceId,
        string ResourceName,
        string ResourceTypeName,
        string Location,
        DateTime Start,
        DateTime End,
        bool CheckedIn,
        List<Guid> Coming);

    private sealed record Visit(Guid PersonId, Booking Booking, bool Invited);
}
