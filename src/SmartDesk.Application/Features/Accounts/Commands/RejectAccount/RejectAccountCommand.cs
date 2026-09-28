using MediatR;

namespace SmartDesk.Application.Features.Accounts.Commands.RejectAccount;

// Admin only. Deletes a waiting sign-up, so the email can be used again, and tells them.
public sealed record RejectAccountCommand(Guid Id) : IRequest;
