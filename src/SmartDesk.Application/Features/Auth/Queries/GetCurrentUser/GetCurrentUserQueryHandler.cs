using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;

namespace SmartDesk.Application.Features.Auth.Queries.GetCurrentUser;

public sealed class GetCurrentUserQueryHandler(ICurrentUserService currentUser, IIdentityService identityService)
    : IRequestHandler<GetCurrentUserQuery, CurrentUserDto>
{
    public async Task<CurrentUserDto> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();

        // A still-valid token can outlive its user (deleted after the token was issued).
        var user = await identityService.FindByIdAsync(userId, cancellationToken)
            ?? throw new UnauthorizedException();

        return new CurrentUserDto(user.Id, user.FirstName, user.LastName, user.Email, user.Roles);
    }
}
