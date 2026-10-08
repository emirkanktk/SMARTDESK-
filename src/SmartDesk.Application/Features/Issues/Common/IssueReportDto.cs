using System.Linq.Expressions;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Issues.Common;

// A reported problem (ADR-0037), also the payload of the IssueReported and IssueUpdated events.
// The photo itself is fetched on its own (GET /api/issues/{id}/photo), when there is one.
public sealed record IssueReportDto(
    Guid Id,
    Guid ResourceId,
    string ResourceName,
    IssueCategory Category,
    string Description,
    bool IsBlocking,
    IssueStatus Status,
    bool HasPhoto,
    Guid ReportedByUserId,
    DateTime CreatedAt,
    DateTime? ResolvedAt)
{
    // "Zeynep Kaya"; null when the account is gone. Filled afterwards: accounts live in Identity.
    public string? ReportedByName { get; init; }

    public static readonly Expression<Func<IssueReport, IssueReportDto>> Projection = i => new IssueReportDto(
        i.Id,
        i.ResourceId,
        i.Resource.Name,
        i.Category,
        i.Description,
        i.IsBlocking,
        i.Status,
        i.Photo != null,
        i.ReportedByUserId,
        i.CreatedAt,
        i.ResolvedAt);
}
