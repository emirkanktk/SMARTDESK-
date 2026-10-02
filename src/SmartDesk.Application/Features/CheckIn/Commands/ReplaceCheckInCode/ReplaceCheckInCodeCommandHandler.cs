using MediatR;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.CheckIn.Common;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.CheckIn.Commands.ReplaceCheckInCode;

public sealed class ReplaceCheckInCodeCommandHandler(
    IApplicationDbContext db, ICurrentUserService currentUser, IFrontendLinks links)
    : IRequestHandler<ReplaceCheckInCodeCommand, CheckInCodeDto>
{
    public async Task<CheckInCodeDto> Handle(ReplaceCheckInCodeCommand request, CancellationToken cancellationToken)
    {
        var resource = await CheckInCodes.FindManageableAsync(db, currentUser, request.ResourceId, cancellationToken);

        resource.CheckInCode = CheckInRules.NewCode();
        await db.SaveChangesAsync(cancellationToken);

        return CheckInCodes.ToDto(resource, links);
    }
}
