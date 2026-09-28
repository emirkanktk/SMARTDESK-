using MediatR;

namespace SmartDesk.Application.Features.Accounts.Commands.RemoveTeamMember;

// Manager only. Takes a member out of the caller's team; the account itself stays, without a
// team, until an Admin moves it to another one or switches it off.
public sealed record RemoveTeamMemberCommand(Guid Id) : IRequest;
