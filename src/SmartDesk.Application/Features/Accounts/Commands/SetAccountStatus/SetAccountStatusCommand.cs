using MediatR;
using SmartDesk.Application.Common.Models;

namespace SmartDesk.Application.Features.Accounts.Commands.SetAccountStatus;

// Switches an account off or back on: an Admin for anyone else, a Manager for the members of
// their own team (ADR-0020). A disabled account's tokens stop working on its next request.
public sealed record SetAccountStatusCommand(Guid Id, bool Enabled) : IRequest<UserAccount>;
