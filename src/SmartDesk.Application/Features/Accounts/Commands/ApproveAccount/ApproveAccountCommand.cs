using System.Text.Json.Serialization;
using MediatR;
using SmartDesk.Application.Common.Models;

namespace SmartDesk.Application.Features.Accounts.Commands.ApproveAccount;

// Lets a waiting sign-up in, and tells them by email (ADR-0020, ADR-0021). An Admin approves any
// sign-up; a Manager approves requests to join their own team. ManagerId (Admin only) puts an
// Employee into a team on the way - e.g. one whose manager's email didn't match anyone.
public sealed record ApproveAccountCommand(Guid? ManagerId = null) : IRequest<UserAccount>
{
    // Comes from the route, not the body.
    [JsonIgnore]
    public Guid Id { get; init; }
}
