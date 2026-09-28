using System.Linq.Expressions;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Reservations.Common;

// Also the payload of the ReservationCreated / ReservationCancelled SignalR events, so it
// carries enough (resource name, type, times, status) for a screen to patch itself. The type
// comes as an id too, so a screen filtered by type can place a live event without looking up
// the resource first.
public sealed record ReservationDto(
    Guid Id,
    Guid ResourceId,
    string ResourceName,
    Guid ResourceTypeId,
    string ResourceTypeName,
    string ResourceLocation,
    Guid UserId,
    DateTime StartDateTime,
    DateTime EndDateTime,
    ReservationStatus Status,
    string? Note,
    int? ReminderMinutes)
{
    // Who booked it ("Ayşe Yılmaz"), for calendars, the dashboard and live events. Not part
    // of Projection - users live in Identity, outside IApplicationDbContext - so handlers
    // fill it afterwards with WithUserNamesAsync.
    public string? UserDisplayName { get; init; }

    public static readonly Expression<Func<Reservation, ReservationDto>> Projection = r => new ReservationDto(
        r.Id,
        r.ResourceId,
        r.Resource.Name,
        r.Resource.ResourceTypeId,
        r.Resource.ResourceType.Name,
        r.Resource.Location,
        r.UserId,
        r.StartDateTime,
        r.EndDateTime,
        r.Status,
        r.Note,
        r.ReminderMinutes);
}
