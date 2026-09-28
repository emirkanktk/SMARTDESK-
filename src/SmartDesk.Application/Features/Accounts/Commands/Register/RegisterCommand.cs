using MediatR;

namespace SmartDesk.Application.Features.Accounts.Commands.Register;

// Public sign-up (ADR-0020, ADR-0021). Without ManagerEmail it signs up a Manager, who waits for
// an Admin's approval. With it, an Employee asking to join that manager's team, who waits for
// that manager's approval - or an Admin's, when the email isn't an active manager's. Always 202
// once the input is valid, whether or not the email already has an account.
public sealed record RegisterCommand(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string? ManagerEmail = null) : IRequest;
