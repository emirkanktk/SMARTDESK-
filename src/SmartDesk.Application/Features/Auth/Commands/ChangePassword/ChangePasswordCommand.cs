using MediatR;
using SmartDesk.Application.Features.Auth.Commands.Login;

namespace SmartDesk.Application.Features.Auth.Commands.ChangePassword;

// The signed-in user replaces their own password (ADR-0021) - by choice, or because someone
// else set a temporary one. Every other session ends, so the answer carries a fresh token for
// this one.
public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword) : IRequest<LoginResponse>;
