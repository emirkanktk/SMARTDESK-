using SmartDesk.Application.Common.Interfaces;

namespace SmartDesk.Application.Features.Reservations.Common;

public static class ReservationDtoExtensions
{
    // One lookup for the whole list, however many reservations share an owner or an attendee.
    public static async Task<IReadOnlyList<ReservationDto>> WithUserNamesAsync(
        this IIdentityService identityService,
        IReadOnlyList<ReservationDto> reservations,
        CancellationToken cancellationToken)
    {
        if (reservations.Count == 0)
        {
            return reservations;
        }

        var names = await identityService.GetDisplayNamesAsync(
            reservations
                .SelectMany(r => r.Attendees.Select(a => a.Id)
                    .Append(r.UserId)
                    .Concat(r.CheckedInBy is { } by ? [by] : [])
                    .Concat(r.DecidedBy is { } admin ? [admin] : []))
                .Distinct()
                .ToList(),
            cancellationToken);

        return reservations
            .Select(r => r with
            {
                UserDisplayName = names.GetValueOrDefault(r.UserId),
                CheckedInByName = r.CheckedInBy is { } by ? names.GetValueOrDefault(by) : null,
                DecidedByName = r.DecidedBy is { } admin ? names.GetValueOrDefault(admin) : null,
                Attendees = r.Attendees
                    .Select(a => a with { Name = names.GetValueOrDefault(a.Id) })
                    .OrderBy(a => a.Name ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                    .ToList(),
            })
            .ToList();
    }

    public static async Task<ReservationDto> WithUserNameAsync(
        this IIdentityService identityService,
        ReservationDto reservation,
        CancellationToken cancellationToken) =>
        (await identityService.WithUserNamesAsync([reservation], cancellationToken))[0];
}
