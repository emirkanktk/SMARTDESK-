using MediatR;

namespace SmartDesk.Application.Features.Accounts.Commands.RejectAccount;

// Deletes a waiting sign-up, so the email can be used again, and tells them. An Admin rejects
// any sign-up; a Manager rejects requests to join their own team.
public sealed record RejectAccountCommand(Guid Id) : IRequest;
