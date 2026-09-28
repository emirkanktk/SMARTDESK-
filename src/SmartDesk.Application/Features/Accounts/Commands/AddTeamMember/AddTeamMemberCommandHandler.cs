using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Accounts.Common;
using SmartDesk.Domain.Constants;

namespace SmartDesk.Application.Features.Accounts.Commands.AddTeamMember;

public sealed class AddTeamMemberCommandHandler(ICurrentUserService currentUser, AccountInvitations invitations)
    : IRequestHandler<AddTeamMemberCommand, UserAccount>
{
    public Task<UserAccount> Handle(AddTeamMemberCommand request, CancellationToken cancellationToken)
    {
        var managerId = currentUser.UserId ?? throw new UnauthorizedException();

        return invitations.InviteAsync(
            request.FirstName, request.LastName, request.Email, Roles.Employee, managerId, cancellationToken);
    }
}
