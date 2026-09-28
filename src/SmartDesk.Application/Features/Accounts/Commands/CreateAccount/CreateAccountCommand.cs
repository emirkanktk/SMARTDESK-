using MediatR;
using SmartDesk.Application.Common.Models;

namespace SmartDesk.Application.Features.Accounts.Commands.CreateAccount;

// Admin only. Adds an account of any role with a temporary password the Admin hands over; its
// owner must choose their own at first sign-in (ADR-0021). ManagerId puts a new Employee
// straight into a Manager's team.
public sealed record CreateAccountCommand(
    string FirstName,
    string LastName,
    string Email,
    string Role,
    string Password,
    Guid? ManagerId = null) : IRequest<UserAccount>;
