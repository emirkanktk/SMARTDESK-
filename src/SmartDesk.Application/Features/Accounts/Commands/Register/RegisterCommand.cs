using MediatR;

namespace SmartDesk.Application.Features.Accounts.Commands.Register;

// Signs up a Manager, who waits for an Admin's approval (ADR-0020). Always 202 once the input
// is valid, whether or not the email already has an account - that owner gets an email saying so.
public sealed record RegisterCommand(string FirstName, string LastName, string Email, string Password) : IRequest;
