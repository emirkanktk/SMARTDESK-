using System.Text.Json.Serialization;
using MediatR;
using SmartDesk.Application.Common.Models;

namespace SmartDesk.Application.Features.Accounts.Commands.ChangeAccountRole;

// Admin only (PATCH /api/users/{id}/role). Takes effect on the account's next request.
public sealed record ChangeAccountRoleCommand(string Role) : IRequest<UserAccount>
{
    // Comes from the route, not the body.
    [JsonIgnore]
    public Guid Id { get; init; }
}
