using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Auth.Queries.GetCurrentUser;

namespace SmartDesk.Application.Features.Auth.Commands.UpdateProfile;

public sealed class UpdateProfileCommandHandler(ICurrentUserService currentUser, IIdentityService identityService)
    : IRequestHandler<UpdateProfileCommand, CurrentUserDto>
{
    public async Task<CurrentUserDto> Handle(UpdateProfileCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();

        await identityService.RenameAsync(userId, request.FirstName.Trim(), request.LastName.Trim(), cancellationToken);

        // The profile as it is now, as GET /api/auth/me answers it.
        var user = await identityService.FindByIdAsync(userId, cancellationToken)
            ?? throw new UnauthorizedException();
        return new CurrentUserDto(user.Id, user.FirstName, user.LastName, user.Email, user.Roles);
    }
}
