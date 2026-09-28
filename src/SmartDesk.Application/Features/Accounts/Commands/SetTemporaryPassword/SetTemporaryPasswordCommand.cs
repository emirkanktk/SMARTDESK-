using System.Text.Json.Serialization;
using MediatR;
using SmartDesk.Application.Common.Models;

namespace SmartDesk.Application.Features.Accounts.Commands.SetTemporaryPassword;

// Gives an account a temporary password that its owner must replace at their next sign-in
// (ADR-0021): an Admin for anyone else, a Manager for the members of their own team. Every
// session of the account ends, and its owner is emailed that it happened.
public sealed record SetTemporaryPasswordCommand(string Password) : IRequest<UserAccount>
{
    // Comes from the route, not the body.
    [JsonIgnore]
    public Guid Id { get; init; }
}
