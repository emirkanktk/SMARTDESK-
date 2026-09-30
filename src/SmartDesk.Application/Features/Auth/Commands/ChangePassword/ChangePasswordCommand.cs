using MediatR;
using SmartDesk.Application.Features.Auth.Common;

namespace SmartDesk.Application.Features.Auth.Commands.ChangePassword;

// The signed-in user replaces their own password (ADR-0021) - by choice, or because someone
// else set a temporary one. Every session ends, so the answer starts a new one here (ADR-0026).
public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword) : IRequest<SignedIn>;
