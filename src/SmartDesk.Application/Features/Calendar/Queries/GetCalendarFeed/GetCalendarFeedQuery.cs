using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Localization;
using SmartDesk.Application.Features.Calendar.Common;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Enums;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.Calendar.Queries.GetCalendarFeed;

// Someone's personal calendar feed (ADR-0048), asked for by their calendar app with the link's
// token rather than a sign-in: their bookings and the meetings they're invited to and haven't
// turned down, from CalendarFeedRules.DaysBack ago on - those still waiting for approval as
// tentative (ADR-0049). What's cancelled is left out, so it goes
// from the calendar at its next fetch. Null - a 404 - for a token that isn't a link's, and for
// a link whose account is disabled. Notes when it was fetched, for its owner to see.
public sealed record GetCalendarFeedQuery(string? Token) : IRequest<CalendarDocument?>;

public sealed class GetCalendarFeedQueryHandler(
    IApplicationDbContext db,
    IUserAccounts accounts,
    IIdentityService identityService,
    IFrontendLinks links,
    ICheckInTimes checkInTimes,
    TimeProvider timeProvider) : IRequestHandler<GetCalendarFeedQuery, CalendarDocument?>
{
    public async Task<CalendarDocument?> Handle(GetCalendarFeedQuery request, CancellationToken cancellationToken)
    {
        if (!CalendarFeedRules.LooksLikeToken(request.Token))
        {
            return null;
        }

        var hash = CalendarFeedRules.HashToken(request.Token!);
        var feed = await db.CalendarFeeds.SingleOrDefaultAsync(f => f.TokenHash == hash, cancellationToken);
        var owner = feed is null ? null : await accounts.FindAsync(feed.UserId, cancellationToken);
        if (feed is null || owner is not { Status: AccountStatus.Active })
        {
            return null;
        }

        // A calendar app asks in its own language, if any: the feed is written in its owner's,
        // like their emails (ADR-0023). Set for the rest of this request only.
        CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture = Languages.CultureFor(owner.Language);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var from = now.AddDays(-CalendarFeedRules.DaysBack);
        var userId = feed.UserId;
        var reservations = await db.Reservations
            .AsNoTracking()
            .Where(r => r.Status != ReservationStatus.Cancelled && r.EndDateTime > from)
            .Where(r => r.UserId == userId
                || r.Attendees.Any(a => a.UserId == userId && a.Response != AttendeeResponse.Declined))
            .OrderBy(r => r.StartDateTime)
            .Select(ReservationDto.Projection)
            .ToListAsync(cancellationToken);
        var named = await identityService.WithUserNamesAsync(reservations, cancellationToken);

        feed.LastFetchedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        var entries = named.Select(r => CalendarEntries.For(r, userId, links, checkInTimes.Grace)).ToList();
        return new CalendarDocument(Messages.Calendar_Name, now, entries) { RefreshInterval = CalendarFeedRules.RefreshInterval };
    }
}
