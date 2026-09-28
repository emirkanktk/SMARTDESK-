using MediatR;
using SmartDesk.Application.Common.Models;

namespace SmartDesk.Application.Features.Accounts.Commands.AddTeamMember;

// Manager only. Adds an Employee to the caller's own team, with a temporary password the
// Manager hands over; the member must choose their own at first sign-in (ADR-0021).
public sealed record AddTeamMemberCommand(string FirstName, string LastName, string Email, string Password)
    : IRequest<UserAccount>;
