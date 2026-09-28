using MediatR;
using SmartDesk.Application.Common.Models;

namespace SmartDesk.Application.Features.Accounts.Queries.GetMyTeam;

// Manager only. The caller's team: requests to join it first, then the members by name.
public sealed record GetMyTeamQuery : IRequest<IReadOnlyList<UserAccount>>;
