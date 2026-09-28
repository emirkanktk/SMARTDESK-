using MediatR;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Accounts.Queries.GetAccounts;

public sealed class GetAccountsQueryHandler(IUserAccounts accounts)
    : IRequestHandler<GetAccountsQuery, IReadOnlyList<UserAccount>>
{
    public async Task<IReadOnlyList<UserAccount>> Handle(GetAccountsQuery request, CancellationToken cancellationToken)
    {
        var list = await accounts.ListAsync(
            new UserFilter(request.Status, request.Role, Search: request.Search), cancellationToken);

        return list
            .OrderByDescending(a => a.Status == AccountStatus.PendingApproval)
            .ThenBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(a => a.Email, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
