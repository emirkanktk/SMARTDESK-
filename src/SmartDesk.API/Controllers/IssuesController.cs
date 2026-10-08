using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartDesk.Application.Features.Issues.Commands.UpdateIssueStatus;
using SmartDesk.Application.Features.Issues.Common;
using SmartDesk.Application.Features.Issues.Queries.GetIssuePhoto;
using SmartDesk.Application.Features.Issues.Queries.GetIssueReports;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Enums;

namespace SmartDesk.API.Controllers;

// Reported problems (ADR-0037), for those who look after resources. A problem is reported on
// its resource: POST /api/resources/{id}/issues.
[ApiController]
[Route("api/issues")]
[Authorize]
public sealed class IssuesController(ISender sender) : ControllerBase
{
    // Every report for an Admin; for a Manager, those on their team's own resources.
    [HttpGet]
    [Authorize(Roles = Roles.AdminOrManager)]
    [ProducesResponseType<IReadOnlyList<IssueReportDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IReadOnlyList<IssueReportDto>> GetAll(
        [FromQuery] IssueStatus? status,
        [FromQuery] Guid? resourceId,
        CancellationToken cancellationToken) =>
        sender.Send(new GetIssueReportsQuery(status, resourceId), cancellationToken);

    // Open, InProgress or Resolved; resolving tells whoever reported it.
    [HttpPatch("{id:guid}")]
    [Authorize(Roles = Roles.AdminOrManager)]
    [ProducesResponseType<IssueReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IssueReportDto> UpdateStatus(Guid id, UpdateIssueStatusCommand command, CancellationToken cancellationToken) =>
        sender.Send(command with { Id = id }, cancellationToken);

    // For anyone who may use the resource, like its own photo (ADR-0027). A report's photo never
    // changes, so the browser may keep it.
    [HttpGet("{id:guid}/photo")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "image/jpeg", "image/png", "image/webp")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPhoto(Guid id, CancellationToken cancellationToken)
    {
        var photo = await sender.Send(new GetIssuePhotoQuery(id), cancellationToken);

        Response.Headers.CacheControl = "private, max-age=86400";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(photo.Data, photo.ContentType);
    }
}
