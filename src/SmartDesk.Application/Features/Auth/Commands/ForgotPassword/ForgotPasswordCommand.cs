using MediatR;

namespace SmartDesk.Application.Features.Auth.Commands.ForgotPassword;

// Always ends in 202 Accepted, whether or not an account has this email (ADR-0019).
public sealed record ForgotPasswordCommand(string Email) : IRequest;
