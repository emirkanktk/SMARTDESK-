using MediatR;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.CheckIn.Common;

namespace SmartDesk.Application.Features.CheckIn.Queries.GetCheckInCode;

public sealed class GetCheckInCodeQueryHandler(
    IApplicationDbContext db, ICurrentUserService currentUser, IFrontendLinks links)
    : IRequestHandler<GetCheckInCodeQuery, CheckInCodeDto>
{
    public async Task<CheckInCodeDto> Handle(GetCheckInCodeQuery request, CancellationToken cancellationToken)
    {
        var resource = await CheckInCodes.FindManageableAsync(db, currentUser, request.ResourceId, cancellationToken);
        return CheckInCodes.ToDto(resource, links);
    }
}
