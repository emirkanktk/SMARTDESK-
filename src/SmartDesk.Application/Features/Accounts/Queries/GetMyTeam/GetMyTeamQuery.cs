using MediatR;
using SmartDesk.Application.Common.Models;

namespace SmartDesk.Application.Features.Accounts.Queries.GetMyTeam;

// Manager only. The members of the caller's team, by name.
public sealed record GetMyTeamQuery : IRequest<IReadOnlyList<UserAccount>>;
