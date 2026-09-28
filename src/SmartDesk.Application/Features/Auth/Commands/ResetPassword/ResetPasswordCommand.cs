using MediatR;

namespace SmartDesk.Application.Features.Auth.Commands.ResetPassword;

// Email and token come from the link in the reset email.
public sealed record ResetPasswordCommand(string Email, string Token, string NewPassword) : IRequest;
