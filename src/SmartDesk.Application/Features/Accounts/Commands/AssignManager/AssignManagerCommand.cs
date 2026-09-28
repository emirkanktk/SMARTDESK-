using System.Text.Json.Serialization;
using MediatR;
using SmartDesk.Application.Common.Models;

namespace SmartDesk.Application.Features.Accounts.Commands.AssignManager;

// Admin only (PATCH /api/users/{id}/manager). Moves an Employee into a Manager's team, or out
// of every team when ManagerId is null.
public sealed record AssignManagerCommand(Guid? ManagerId) : IRequest<UserAccount>
{
    // Comes from the route, not the body.
    [JsonIgnore]
    public Guid Id { get; init; }
}
