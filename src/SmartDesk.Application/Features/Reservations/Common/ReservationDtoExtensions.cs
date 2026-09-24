using SmartDesk.Application.Common.Interfaces;

namespace SmartDesk.Application.Features.Reservations.Common;

public static class ReservationDtoExtensions
{
    // One lookup for the whole list, however many reservations share an owner.
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
            reservations.Select(r => r.UserId).Distinct().ToList(), cancellationToken);

        return reservations
            .Select(r => r with { UserDisplayName = names.GetValueOrDefault(r.UserId) })
            .ToList();
    }

    public static async Task<ReservationDto> WithUserNameAsync(
        this IIdentityService identityService,
        ReservationDto reservation,
        CancellationToken cancellationToken) =>
        (await identityService.WithUserNamesAsync([reservation], cancellationToken))[0];
}
