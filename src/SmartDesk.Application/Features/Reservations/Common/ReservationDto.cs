using System.Linq.Expressions;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Reservations.Common;

// Also the payload of the ReservationCreated / ReservationCancelled SignalR events, so it
// carries enough (resource name, type, times, status) for a screen to patch itself.
public sealed record ReservationDto(
    Guid Id,
    Guid ResourceId,
    string ResourceName,
    string ResourceTypeName,
    string ResourceLocation,
    Guid UserId,
    DateTime StartDateTime,
    DateTime EndDateTime,
    ReservationStatus Status,
    string? Note)
{
    public static readonly Expression<Func<Reservation, ReservationDto>> Projection = r => new ReservationDto(
        r.Id,
        r.ResourceId,
        r.Resource.Name,
        r.Resource.ResourceType.Name,
        r.Resource.Location,
        r.UserId,
        r.StartDateTime,
        r.EndDateTime,
        r.Status,
        r.Note);
}
