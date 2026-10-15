using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Resources.Common;

public static class ResourceManagement
{
    public static string NotYoursMessage => Messages.Resources_NotYours;

    // For a resource the caller can already see (loaded with its Teams): may they edit it,
    // change its status or block it? 403 otherwise, since they know it exists (ADR-0022).
    public static void RequireManageable(Resource resource, ICurrentUserService user)
    {
        if (!ResourceAccess.CanManage(resource.Teams.Select(t => t.TeamId).ToList(), user))
        {
            throw new ForbiddenAccessException(NotYoursMessage);
        }
    }

    // A resource going into maintenance or being switched off would leave people holding
    // bookings they can't use (issue #41). Like a block (ADR-0015), it waits until the
    // Reservations still to come - booked, or waiting for approval (ADR-0049) - are cancelled or
    // moved; running ones don't count.
    // Call inside a transaction that holds the resource's booking lock, so no booking can
    // slip in between the count and the change.
    public static async Task RequireNoUpcomingReservationsAsync(
        IApplicationDbContext db, Resource resource, ResourceStatus newStatus, DateTime now, CancellationToken cancellationToken)
    {
        if (newStatus == ResourceStatus.Active || newStatus == resource.Status)
        {
            return;
        }

        var upcoming = await db.Reservations.CountAsync(
            r => r.ResourceId == resource.Id && r.Status != ReservationStatus.Cancelled && r.StartDateTime > now,
            cancellationToken);
        if (upcoming > 0)
        {
            throw new ConflictException(string.Format(Messages.Resources_HasUpcomingReservations, upcoming, resource.Name))
            {
                Code = ConflictCodes.ResourceHasReservations,
            };
        }
    }
}
