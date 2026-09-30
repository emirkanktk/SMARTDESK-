using MediatR;
using SmartDesk.Application.Features.Auth.Common;

namespace SmartDesk.Application.Features.Auth.Commands.Login;

public sealed record LoginCommand(string Email, string Password) : IRequest<SignedIn>;
