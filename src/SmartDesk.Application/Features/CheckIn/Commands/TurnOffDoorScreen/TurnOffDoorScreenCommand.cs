using MediatR;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.CheckIn.Common;

namespace SmartDesk.Application.Features.CheckIn.Commands.TurnOffDoorScreen;

// No more door screen for the resource (ADR-0034): its link stops working, and the printed QR
// code checks in again. Doing it twice changes nothing.
public sealed record TurnOffDoorScreenCommand(Guid ResourceId) : IRequest;

public sealed class TurnOffDoorScreenCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<TurnOffDoorScreenCommand>
{
    public async Task Handle(TurnOffDoorScreenCommand request, CancellationToken cancellationToken)
    {
        var resource = await CheckInCodes.FindManageableAsync(db, currentUser, request.ResourceId, cancellationToken);
        if (resource.DoorScreenTokenHash is null)
        {
            return;
        }

        resource.DoorScreenTokenHash = null;
        resource.DoorScreenSetUpAt = null;
        await db.SaveChangesAsync(cancellationToken);
    }
}
