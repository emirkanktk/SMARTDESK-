using MediatR;
using SmartDesk.Application.Features.Issues.Common;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Issues.Commands.CreateIssueReport;

// Reports a problem with a resource (POST /api/resources/{id}/issues, multipart, ADR-0037).
// Anyone who may use the resource may report one, booked or not. The photo is optional.
// Category is nullable so that a form without one is refused rather than read as Display.
public sealed record CreateIssueReportCommand(
    Guid ResourceId,
    IssueCategory? Category,
    string Description,
    bool IsBlocking,
    byte[]? Photo) : IRequest<IssueReportDto>;
