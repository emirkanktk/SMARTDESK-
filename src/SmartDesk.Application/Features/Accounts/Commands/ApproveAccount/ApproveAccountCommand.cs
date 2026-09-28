using MediatR;
using SmartDesk.Application.Common.Models;

namespace SmartDesk.Application.Features.Accounts.Commands.ApproveAccount;

// Admin only. Lets a waiting Manager sign-up in, and tells them by email.
public sealed record ApproveAccountCommand(Guid Id) : IRequest<UserAccount>;
