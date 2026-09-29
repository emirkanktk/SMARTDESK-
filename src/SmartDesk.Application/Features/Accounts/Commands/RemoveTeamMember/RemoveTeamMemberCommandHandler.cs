using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Accounts.Commands.RemoveTeamMember;

public sealed class RemoveTeamMemberCommandHandler(IUserAccounts accounts, ICurrentUserService currentUser)
    : IRequestHandler<RemoveTeamMemberCommand>
{
    public async Task Handle(RemoveTeamMemberCommand request, CancellationToken cancellationToken)
    {
        var managerId = currentUser.UserId ?? throw new UnauthorizedException();
        var member = await accounts.FindAsync(request.Id, cancellationToken);

        // Not found rather than forbidden, so a Manager can't probe for accounts outside their team.
        if (member is null || member.ManagerId != managerId)
        {
            throw new NotFoundException("Account", request.Id);
        }

        // A request to join isn't a member yet: rejecting it tells the applicant, removing wouldn't.
        if (member.Status == AccountStatus.PendingApproval)
        {
            throw new ConflictException(Messages.Accounts_JoinRequestPending);
        }

        await accounts.SetManagerAsync(member.Id, null, cancellationToken);
    }
}
