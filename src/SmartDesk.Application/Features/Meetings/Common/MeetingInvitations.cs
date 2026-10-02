using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Inbox.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Meetings.Common;

// Who's invited to a booking, and the messages that tell them (ADR-0032). A handler calls
// CheckAsync while it holds the booking's locks, adds what Write gives to the same save, and
// calls SendAsync once that's committed, so a message never announces a change that rolled back.
public sealed class MeetingInvitations(
    IApplicationDbContext db,
    Invitees invitees,
    ICurrentUserService currentUser,
    IIdentityService identityService,
    IInboxNotifier notifier,
    TimeProvider timeProvider)
{
    // `requested`: the list the booking should have (null or empty: nobody). `current`: who's
    // on it now, none for a new booking. Only people added must be ones the caller may invite;
    // someone already on the list stays, even if they've since left the team.
    public async Task<AttendeeChange> CheckAsync(
        Guid organizerId,
        Guid resourceId,
        IReadOnlyCollection<Guid>? requested,
        IReadOnlyCollection<Guid> current,
        CancellationToken cancellationToken)
    {
        var wanted = (requested ?? []).Where(id => id != organizerId).Distinct().ToList();
        var added = wanted.Except(current).ToList();
        var removed = current.Except(wanted).ToList();
        if (wanted.Count == 0 && removed.Count == 0)
        {
            return AttendeeChange.None;
        }

        var resource = await db.Resources
            .AsNoTracking()
            .Where(r => r.Id == resourceId)
            .Select(r => new { r.Name, r.Capacity, Teams = r.Teams.Select(t => t.TeamId).ToList() })
            .SingleAsync(cancellationToken);

        if (added.Count > 0)
        {
            if (!invitees.MayInvite)
            {
                throw Invalid(Messages.Meetings_OnlyManagersInvite);
            }

            var invitable = (await invitees.ListAsync(cancellationToken)).ToDictionary(person => person.Id);
            if (added.Any(id => !invitable.ContainsKey(id)))
            {
                throw Invalid(currentUser.IsInRole(Roles.Admin) ? Messages.Meetings_NotInvitableByAdmin : Messages.Meetings_NotInvitable);
            }

            // Another team's resource isn't there for its members (ADR-0022), so they couldn't
            // even open the meeting. Only an Admin can get here: a Manager's own team may use
            // whatever its manager may book.
            var outsider = added.Select(id => invitable[id]).FirstOrDefault(person => !MayUse(person, resource.Teams));
            if (outsider is not null)
            {
                throw Invalid(string.Format(Messages.Meetings_CantUseResource, outsider.DisplayName, resource.Name));
            }
        }

        // The organizer is in the room too.
        if (wanted.Count + 1 > resource.Capacity)
        {
            throw Invalid(string.Format(Messages.Meetings_RoomTooSmall, resource.Name, resource.Capacity, wanted.Count + 1));
        }

        return new AttendeeChange(wanted, added, removed, resource.Name);
    }

    // Messages of one kind about `reservation` as it is now, to each of `recipients`, added to
    // the context for the caller's save. `senderId` is null when nobody caused it (a release);
    // `reason` is why someone can't come, for a MeetingDeclined.
    public IReadOnlyList<InboxMessage> Write(
        InboxMessageKind kind,
        IEnumerable<Guid> recipients,
        Reservation reservation,
        string resourceName,
        Guid? senderId,
        string? reason = null)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var messages = recipients
            .Select(recipientId => new InboxMessage
            {
                Id = Guid.CreateVersion7(),
                RecipientId = recipientId,
                Kind = kind,
                SenderId = senderId,
                ReservationId = reservation.Id,
                ResourceName = resourceName,
                StartDateTime = reservation.StartDateTime,
                EndDateTime = reservation.EndDateTime,
                Note = reservation.Note,
                Reason = reason,
                CreatedAt = now,
            })
            .ToList();
        db.InboxMessages.AddRange(messages);
        return messages;
    }

    // Tells everyone invited to a booking that's being cancelled or released, who would
    // otherwise turn up to a room that isn't theirs any more.
    public async Task<IReadOnlyList<InboxMessage>> WriteCancellationAsync(
        Reservation reservation, Guid? senderId, CancellationToken cancellationToken)
    {
        var attendees = await db.ReservationAttendees
            .Where(a => a.ReservationId == reservation.Id)
            .Select(a => a.UserId)
            .ToListAsync(cancellationToken);
        if (attendees.Count == 0)
        {
            return [];
        }

        var resourceName = await db.Resources
            .Where(r => r.Id == reservation.ResourceId)
            .Select(r => r.Name)
            .SingleAsync(cancellationToken);
        return Write(InboxMessageKind.MeetingCancelled, attendees, reservation, resourceName, senderId);
    }

    // Live, to each recipient's open screens, once the messages are committed.
    public async Task SendAsync(IReadOnlyList<InboxMessage> messages, CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
        {
            return;
        }

        var senders = messages.Select(m => m.SenderId).OfType<Guid>().Distinct().ToList();
        var names = senders.Count == 0
            ? new Dictionary<Guid, string>()
            : await identityService.GetDisplayNamesAsync(senders, cancellationToken);

        await notifier.MessagesSentAsync(
            messages
                .Select(m => InboxMessageDto.From(m) with
                {
                    SenderName = m.SenderId is { } sender ? names.GetValueOrDefault(sender) : null,
                })
                .ToList(),
            cancellationToken);
    }

    private static bool MayUse(UserAccount person, IReadOnlyCollection<Guid> teams) =>
        teams.Count == 0
        || person.Role == Roles.Admin
        || teams.Contains(person.Role == Roles.Manager ? person.Id : person.ManagerId ?? Guid.Empty);

    private static ValidationException Invalid(string message) =>
        new([new ValidationFailure("AttendeeIds", message)]);
}

// What saving a booking does to its list. ResourceName is set whenever the list isn't empty
// or someone was taken off it: the messages need it.
public sealed record AttendeeChange(
    IReadOnlyList<Guid> Attendees,
    IReadOnlyList<Guid> Added,
    IReadOnlyList<Guid> Removed,
    string? ResourceName)
{
    public static readonly AttendeeChange None = new([], [], [], null);

    // Still invited, and invited before this save.
    public IEnumerable<Guid> Kept => Attendees.Except(Added);

    public bool Changed => Added.Count > 0 || Removed.Count > 0;
}
