using MediatR;
using SmartDesk.Application.Common.Models;

namespace SmartDesk.Application.Features.Accounts.Commands.AddTeamMember;

// Manager only. Adds an Employee to the caller's own team; they get an email to choose a password.
public sealed record AddTeamMemberCommand(string FirstName, string LastName, string Email) : IRequest<UserAccount>;
