using FluentValidation;
using SmartDesk.Application.Features.Accounts.Common;

namespace SmartDesk.Application.Features.Accounts.Queries.GetAccounts;

public sealed class GetAccountsQueryValidator : AbstractValidator<GetAccountsQuery>
{
    public GetAccountsQueryValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Role!).AccountRole().When(x => x.Role is not null);
        RuleFor(x => x.Search).MaximumLength(AccountRules.MaxEmailLength);
    }
}
