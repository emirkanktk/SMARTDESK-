using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Extensions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.CheckIn.Common;
using SmartDesk.Application.Features.CheckIn.Queries.GetDoorScreen;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.LobbyScreens.Queries.GetLobby;

// What a lobby screen shows (GET /api/lobby, ADR-0064), asked by the screen itself with its own
// token rather than a user's sign-in: its meeting rooms, each with the rest of today as a door
// screen shows it (times only: a lobby is no place for names), how many desks are free now on each
// floor, how many people are in today, and the app's address for its QR code. On the screen's
// floor, or across the office. A token that isn't a screen's is a 404, like a door screen's.
public sealed record GetLobbyQuery(string? Token, string? TimeZone = null) : IRequest<LobbyDto>;

// PeopleToday: everyone a booking brings in today, or a meeting they said they'd come to, across
// the office whatever the screen's floor (ADR-0063 lists them; a screen only counts them).
public sealed record LobbyDto(
    string ScreenName,
    string? Location,
    DateTime Now,
    string AppUrl,
    int PeopleToday,
    IReadOnlyList<LobbyRoomDto> Rooms,
    IReadOnlyList<LobbyDesksDto> Desks);

// A meeting room, out of use or not, with the rest of today's bookings - at most
// LobbyScreenRules.MaxBookingsPerRoom - and blocked windows, earliest first.
public sealed record LobbyRoomDto(
    Guid Id,
    string Name,
    string Location,
    int Capacity,
    ResourceStatus Status,
    IReadOnlyList<DoorBookingDto> Bookings,
    IReadOnlyList<DoorBlockDto> Blocks);

// A floor's desks in use: how many there are, and how many of them nobody has right now.
public sealed record LobbyDesksDto(string Location, int Free, int Total);

public sealed class GetLobbyQueryValidator : AbstractValidator<GetLobbyQuery>
{
    public GetLobbyQueryValidator()
    {
        RuleFor(x => x.TimeZone)
            .Must(id => TimeZoneInfo.TryFindSystemTimeZoneById(id!, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.TimeZone))
            .WithMessage(_ => Messages.Validation_UnknownTimeZone);
    }
}

public sealed class GetLobbyQueryHandler(IApplicationDbContext db, IFrontendLinks links, TimeProvider timeProvider)
    : IRequestHandler<GetLobbyQuery, LobbyDto>
{
    public async Task<LobbyDto> Handle(GetLobbyQuery request, CancellationToken cancellationToken)
    {
        var hash = LobbyScreenRules.IsWellFormed(request.Token) ? LobbyScreenRules.HashToken(request.Token!) : null;
        var screen = hash is null
            ? null
            : await db.LobbyScreens.SingleOrDefaultAsync(s => s.TokenHash == hash, cancellationToken);
        if (screen is null)
        {
            throw new NotFoundException(nameof(LobbyScreen), "token");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (screen.LastSeenAt is not { } seen || now - seen >= LobbyScreenRules.SeenEvery)
        {
            screen.LastSeenAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        var zone = string.IsNullOrWhiteSpace(request.TimeZone)
            ? TimeZoneInfo.Utc
            : TimeZoneInfo.FindSystemTimeZoneById(request.TimeZone);
        var (dayStart, dayEnd) = zone.LocalDayBoundsUtc(now);

        // What's in the office still: a retired resource isn't, one in maintenance says so.
        var places = db.Resources.AsNoTracking().Where(r => r.Status != ResourceStatus.Inactive);
        if (screen.Location is { } floor)
        {
            places = places.Where(r => r.Location == floor);
        }

        var rooms = await places
            .Where(r => r.ResourceTypeId == ResourceTypeIds.MeetingRoom)
            .OrderBy(r => r.Location)
            .ThenBy(r => r.Name)
            .Select(r => new { r.Id, r.Name, r.Location, r.Capacity, r.Status })
            .ToListAsync(cancellationToken);
        var days = await DoorDay.LoadManyAsync(
            db, rooms.Select(r => r.Id).ToList(), now, dayEnd, LobbyScreenRules.MaxBookingsPerRoom, cancellationToken);

        // A desk is taken while a booking holds it - waiting for approval too (ADR-0049) - or a
        // blocked window covers it.
        var desks = await places
            .Where(r => r.ResourceTypeId == ResourceTypeIds.Desk && r.Status == ResourceStatus.Active)
            .Select(r => new
            {
                r.Location,
                Taken = r.Reservations.Any(b => b.Status != ReservationStatus.Cancelled && b.StartDateTime <= now && b.EndDateTime > now)
                    || r.Blocks.Any(b => b.StartDateTime <= now && b.EndDateTime > now),
            })
            .ToListAsync(cancellationToken);

        var todays = db.Reservations
            .AsNoTracking()
            .Where(r => r.Status == ReservationStatus.Confirmed && r.StartDateTime < dayEnd && r.EndDateTime > dayStart);
        var owners = await todays.Select(r => r.UserId).Distinct().ToListAsync(cancellationToken);
        var coming = await todays
            .SelectMany(r => r.Attendees.Where(a => a.Response == AttendeeResponse.Accepted).Select(a => a.UserId))
            .Distinct()
            .ToListAsync(cancellationToken);

        return new LobbyDto(
            screen.Name,
            screen.Location,
            now,
            links.Home(),
            owners.Union(coming).Count(),
            rooms.Select(r => new LobbyRoomDto(r.Id, r.Name, r.Location, r.Capacity, r.Status, days[r.Id].Bookings, days[r.Id].Blocks)).ToList(),
            desks
                .GroupBy(d => d.Location)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new LobbyDesksDto(g.Key, g.Count(d => !d.Taken), g.Count()))
                .ToList());
    }
}
