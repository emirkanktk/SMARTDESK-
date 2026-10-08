using System.Text.Json.Serialization;
using MediatR;
using SmartDesk.Application.Features.Issues.Common;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Issues.Commands.UpdateIssueStatus;

// Moves a report on (PATCH /api/issues/{id}, ADR-0037): Open and InProgress back and forth,
// then Resolved, which is final and tells whoever reported it. For those who may manage the
// resource.
public sealed record UpdateIssueStatusCommand(IssueStatus Status) : IRequest<IssueReportDto>
{
    // Comes from the route, not the body.
    [JsonIgnore]
    public Guid Id { get; init; }
}
