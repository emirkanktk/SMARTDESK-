using MediatR;
using SmartDesk.Application.Common.Models;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Accounts.Queries.GetAccounts;

// Admin only. Every account, sign-ups waiting for approval first; Search matches name or email.
public sealed record GetAccountsQuery(AccountStatus? Status = null, string? Role = null, string? Search = null)
    : IRequest<IReadOnlyList<UserAccount>>;
