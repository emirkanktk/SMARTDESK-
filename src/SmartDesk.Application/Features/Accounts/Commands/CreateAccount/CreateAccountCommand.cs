using MediatR;
using SmartDesk.Application.Common.Models;

namespace SmartDesk.Application.Features.Accounts.Commands.CreateAccount;

// Admin only. Adds an account of any role, which its owner activates by choosing a password
// through an emailed link. ManagerId puts a new Employee straight into a Manager's team.
public sealed record CreateAccountCommand(
    string FirstName,
    string LastName,
    string Email,
    string Role,
    Guid? ManagerId = null) : IRequest<UserAccount>;
