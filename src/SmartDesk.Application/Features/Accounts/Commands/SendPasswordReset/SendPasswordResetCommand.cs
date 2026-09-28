using MediatR;

namespace SmartDesk.Application.Features.Accounts.Commands.SendPasswordReset;

// Emails an account's owner a link to choose a new password - an Admin for anyone, a Manager
// for their own team. Nobody else ever sees or sets the password; the old one keeps working
// until the owner uses the link.
public sealed record SendPasswordResetCommand(Guid Id) : IRequest;
