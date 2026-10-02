using MediatR;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.CheckIn.Common;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.CheckIn.Commands.SetUpDoorScreen;

// A link for a screen at the resource's door (ADR-0034), for those who may edit the resource.
// The screen shows a code that changes every 30 seconds, and from now on only that code checks
// in: the printed one stops working. Setting up again gives a new link and cuts off the old
// screen. The token is in the link only; just its hash is kept.
public sealed record SetUpDoorScreenCommand(Guid ResourceId) : IRequest<DoorScreenLinkDto>;

public sealed class SetUpDoorScreenCommandHandler(
    IApplicationDbContext db, ICurrentUserService currentUser, IFrontendLinks links, TimeProvider timeProvider)
    : IRequestHandler<SetUpDoorScreenCommand, DoorScreenLinkDto>
{
    public async Task<DoorScreenLinkDto> Handle(SetUpDoorScreenCommand request, CancellationToken cancellationToken)
    {
        var resource = await CheckInCodes.FindManageableAsync(db, currentUser, request.ResourceId, cancellationToken);

        var token = CheckInRules.NewDoorScreenToken();
        resource.DoorScreenTokenHash = CheckInRules.HashDoorScreenToken(token);
        resource.DoorScreenSetUpAt = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);

        return new DoorScreenLinkDto(links.DoorScreen(resource.Id, token), resource.DoorScreenSetUpAt.Value);
    }
}
