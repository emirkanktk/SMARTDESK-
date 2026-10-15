using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Extensions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.BookingRules.Common;
using SmartDesk.Application.Features.Meetings.Common;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Reservations.Commands.CreateReservationSeries;

public sealed class CreateReservationSeriesCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IIdentityService identityService,
    IReservationNotifier notifier,
    MeetingInvitations invitations,
    TimeProvider timeProvider,
    BookingPolicy policy) : IRequestHandler<CreateReservationSeriesCommand, ReservationSeriesResultDto>
{
    public async Task<ReservationSeriesResultDto> Handle(CreateReservationSeriesCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var rule = request.Recurrence;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(rule.TimeZone);
        var start = request.StartDateTime.AsUtc();
        var end = request.EndDateTime.AsUtc();
        var days = RecurrenceRules.DaysOf(rule, RecurrenceRules.LocalDate(start, zone));
        var dates = RecurrenceRules.Occurrences(start, end, rule.Frequency, rule.Interval, days, rule.Until, zone);

        var series = new ReservationSeries
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Frequency = rule.Frequency,
            Interval = rule.Interval,
            DaysOfWeek = days,
            Until = rule.Until,
            TimeZone = rule.TimeZone,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
        };
        var booked = new List<Reservation>();
        var skipped = new List<SeriesDateDto>();
        IReadOnlyList<InboxMessage> messages;

        // One transaction for every date, under the resource's lock (ADR-0011), so what's checked
        // is what's booked, and either all the dates the booker meant are booked or none are.
        await using (var transaction = await db.BeginTransactionAsync(cancellationToken))
        {
            await db.LockResourceForBookingAsync(request.ResourceId, cancellationToken);

            var resource = await BookingGuard.EnsureResourceBookableAsync(db, currentUser, request.ResourceId, cancellationToken);
            await policy.EnsureCanRepeatAsync(request.ResourceId, currentUser.IsInRole(Roles.Admin), cancellationToken);

            // Its kind's rules (ADR-0046): the first date as any booking, the last no further
            // ahead than allowed. A date outside office hours is skipped like one that isn't free.
            var outside = await policy.CheckSeriesAsync(request.ResourceId, dates, cancellationToken);
            var taken = await BookingGuard.FindConflictsAsync(db, request.ResourceId, resource.Name, dates, null, cancellationToken);
            var conflicts = dates.Select((_, i) => outside[i] ?? taken[i]).ToList();
            var notFree = conflicts.Count(c => c is not null);
            if (notFree == dates.Count)
            {
                throw new ConflictException(string.Format(Messages.Series_NoneFree, resource.Name)) { Code = ConflictCodes.SeriesConflicts };
            }

            if (notFree > 0 && !request.SkipConflicts)
            {
                throw new ConflictException(string.Format(Messages.Series_Conflicts, notFree, dates.Count, resource.Name))
                {
                    Code = ConflictCodes.SeriesConflicts,
                };
            }

            // No more of the kind than its owner may have (ADR-0046), counting the dates booked.
            await policy.EnsureSeriesWithinLimitAsync(userId, request.ResourceId, dates.Count - notFree, cancellationToken);

            // Who may come, and whether they fit (ADR-0032): the same for every date.
            var attendees = await invitations.CheckAsync(userId, request.ResourceId, request.AttendeeIds, [], cancellationToken);
            var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();

            for (var i = 0; i < dates.Count; i++)
            {
                if (conflicts[i] is { } conflict)
                {
                    skipped.Add(new SeriesDateDto(dates[i].Start, dates[i].End, false, conflict.Code, conflict.Reason));
                    continue;
                }

                booked.Add(new Reservation
                {
                    Id = Guid.CreateVersion7(),
                    ResourceId = request.ResourceId,
                    UserId = userId,
                    StartDateTime = dates[i].Start,
                    EndDateTime = dates[i].End,
                    Status = ReservationStatus.Confirmed,
                    Note = note,
                    ReminderMinutes = request.ReminderMinutes,
                    CheckInRequired = resource.CheckInRequired,
                    SeriesId = series.Id,
                    Attendees = attendees.Attendees.Select(id => new ReservationAttendee { UserId = id }).ToList(),
                });
            }

            db.ReservationSeries.Add(series);
            db.Reservations.AddRange(booked);
            // One invitation for the whole series, not one a date.
            messages = invitations.Write(
                InboxMessageKind.SeriesInvitation, attendees.Added, booked[0], resource.Name, userId, series: series);

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        // Only after commit: never announce a booking that could still roll back.
        var ids = booked.Select(r => r.Id).ToList();
        var dtos = await db.Reservations
            .AsNoTracking()
            .Where(r => ids.Contains(r.Id))
            .OrderBy(r => r.StartDateTime)
            .Select(ReservationDto.Projection)
            .ToListAsync(cancellationToken);
        var named = await identityService.WithUserNamesAsync(dtos, cancellationToken);

        foreach (var dto in named)
        {
            await notifier.ReservationCreatedAsync(dto, cancellationToken);
        }

        await invitations.SendAsync(messages, cancellationToken);

        return new ReservationSeriesResultDto(named[0].Series!, named, skipped);
    }
}
