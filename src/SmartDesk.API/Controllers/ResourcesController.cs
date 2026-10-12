using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using SmartDesk.API.RateLimiting;
using SmartDesk.Application.Features.CheckIn.Commands.CheckIn;
using SmartDesk.Application.Features.CheckIn.Commands.ReplaceCheckInCode;
using SmartDesk.Application.Features.CheckIn.Commands.SetUpDoorScreen;
using SmartDesk.Application.Features.CheckIn.Commands.TurnOffDoorScreen;
using SmartDesk.Application.Features.CheckIn.Common;
using SmartDesk.Application.Features.CheckIn.Queries.GetCheckInCode;
using SmartDesk.Application.Features.Issues.Commands.CreateIssueReport;
using SmartDesk.Application.Features.Issues.Common;
using SmartDesk.Application.Features.Issues.Queries.GetOpenIssues;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Features.Resources.Commands.ChangeResourceStatus;
using SmartDesk.Application.Features.Resources.Commands.CreateResource;
using SmartDesk.Application.Features.Resources.Commands.RemoveResourcePhoto;
using SmartDesk.Application.Features.Resources.Commands.SetResourcePhoto;
using SmartDesk.Application.Features.Resources.Commands.SetResourcePosition;
using SmartDesk.Application.Features.Resources.Commands.SetResourceTeams;
using SmartDesk.Application.Features.Resources.Commands.UpdateResource;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Application.Features.Resources.Queries.GetAvailableResources;
using SmartDesk.Application.Features.Resources.Queries.GetResourceById;
using SmartDesk.Application.Features.Resources.Queries.GetResourcePhoto;
using SmartDesk.Application.Features.Resources.Queries.GetResources;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Enums;

namespace SmartDesk.API.Controllers;

[ApiController]
[Route("api/resources")]
[Authorize]
public sealed class ResourcesController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ResourceDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<ResourceDto>> GetAll(
        [FromQuery] Guid? resourceTypeId,
        [FromQuery] ResourceStatus? status,
        CancellationToken cancellationToken) =>
        sender.Send(new GetResourcesQuery(resourceTypeId, status), cancellationToken);

    [HttpGet("available")]
    [ProducesResponseType<IReadOnlyList<ResourceDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<IReadOnlyList<ResourceDto>> GetAvailable(
        [FromQuery] GetAvailableResourcesQuery query,
        CancellationToken cancellationToken) =>
        sender.Send(query, cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ResourceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ResourceDto> GetById(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetResourceByIdQuery(id), cancellationToken);

    // Admins add any resource; a Manager adds one to their own team (ADR-0022).
    [HttpPost]
    [Authorize(Roles = Roles.AdminOrManager)]
    [ProducesResponseType<ResourceDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ResourceDto>> Create(CreateResourceCommand command, CancellationToken cancellationToken)
    {
        var resource = await sender.Send(command, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = resource.Id }, resource);
    }

    // A Manager may edit only a resource that belongs to their team alone.
    [HttpPut("{id:guid}")]
    [Authorize(Roles = Roles.AdminOrManager)]
    [ProducesResponseType<ResourceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ResourceDto> Update(Guid id, UpdateResourceCommand command, CancellationToken cancellationToken) =>
        sender.Send(command with { Id = id }, cancellationToken);

    [HttpPatch("{id:guid}/status")]
    [Authorize(Roles = Roles.AdminOrManager)]
    [ProducesResponseType<ResourceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ResourceDto> ChangeStatus(Guid id, ChangeResourceStatusCommand command, CancellationToken cancellationToken) =>
        sender.Send(command with { Id = id }, cancellationToken);

    // Which teams may use it; an empty list makes it shared by everyone (ADR-0022).
    [HttpPatch("{id:guid}/teams")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ResourceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ResourceDto> SetTeams(Guid id, SetResourceTeamsCommand command, CancellationToken cancellationToken) =>
        sender.Send(command with { Id = id }, cancellationToken);

    // Places it on its floor's plan, or takes it off with neither x nor y (ADR-0041). Whoever
    // may edit the resource may move it.
    [HttpPut("{id:guid}/position")]
    [Authorize(Roles = Roles.AdminOrManager)]
    [ProducesResponseType<ResourceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ResourceDto> SetPosition(Guid id, SetResourcePositionCommand command, CancellationToken cancellationToken) =>
        sender.Send(command with { Id = id }, cancellationToken);

    // For anyone who may use the resource. The browser keeps it and asks again with the ETag,
    // so an unchanged photo costs a 304 (ADR-0027).
    [HttpGet("{id:guid}/photo")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "image/jpeg", "image/png", "image/webp")]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPhoto(Guid id, CancellationToken cancellationToken)
    {
        var photo = await sender.Send(new GetResourcePhotoQuery(id), cancellationToken);

        Response.Headers.CacheControl = "private, no-cache";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(photo.Data, photo.ContentType, photo.UpdatedAt, new EntityTagHeaderValue($"\"{photo.UpdatedAt.Ticks:x}\""));
    }

    // A JPEG, PNG or WebP of at most 2 MB in the form field "photo"; whoever may edit the
    // resource may change its photo. The request may be larger than the photo, so one a little
    // over the limit still gets the validation message.
    [HttpPut("{id:guid}/photo")]
    [Authorize(Roles = Roles.AdminOrManager)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(ResourcePhotoRules.MaxRequestBytes)]
    [ProducesResponseType<ResourceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ResourceDto> SetPhoto(Guid id, IFormFile? photo, CancellationToken cancellationToken)
    {
        using var data = new MemoryStream();
        if (photo is not null)
        {
            await photo.CopyToAsync(data, cancellationToken);
        }

        return await sender.Send(new SetResourcePhotoCommand(id, data.ToArray()), cancellationToken);
    }

    // Its type's picture shows instead. Removing a photo that isn't there is fine.
    [HttpDelete("{id:guid}/photo")]
    [Authorize(Roles = Roles.AdminOrManager)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemovePhoto(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new RemoveResourcePhotoCommand(id), cancellationToken);

        return NoContent();
    }

    // Checks the caller in to their booking of this resource that's open for it now, with the
    // secret from the resource's QR code (ADR-0030). Checking in twice is fine.
    [HttpPost("{id:guid}/check-in")]
    [EnableRateLimiting(RateLimitingSetup.CheckInPolicy)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ReservationDto> CheckIn(Guid id, CheckInCommand command, CancellationToken cancellationToken) =>
        sender.Send(command with { ResourceId = id }, cancellationToken);

    // The QR code to print, for whoever may edit the resource. The secret is nowhere else.
    [HttpGet("{id:guid}/check-in-code")]
    [Authorize(Roles = Roles.AdminOrManager)]
    [ProducesResponseType<CheckInCodeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<CheckInCodeDto> GetCheckInCode(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetCheckInCodeQuery(id), cancellationToken);

    // A new secret: codes already printed stop working at once.
    [HttpPost("{id:guid}/check-in-code")]
    [Authorize(Roles = Roles.AdminOrManager)]
    [ProducesResponseType<CheckInCodeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<CheckInCodeDto> ReplaceCheckInCode(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new ReplaceCheckInCodeCommand(id), cancellationToken);

    // A link for a screen at the resource's door, with a code that changes every 30 seconds
    // (ADR-0034). From then on only that code checks in. Again: a new link, the old screen cut
    // off. For those who may edit the resource.
    [HttpPost("{id:guid}/door-screen")]
    [ProducesResponseType<DoorScreenLinkDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<DoorScreenLinkDto> SetUpDoorScreen(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new SetUpDoorScreenCommand(id), cancellationToken);

    // No door screen any more: its link stops working, and the printed code checks in again.
    [HttpDelete("{id:guid}/door-screen")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TurnOffDoorScreen(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new TurnOffDoorScreenCommand(id), cancellationToken);
        return NoContent();
    }

    // The problems still open on the resource, for anyone who may use it (ADR-0037).
    [HttpGet("{id:guid}/issues")]
    [ProducesResponseType<IReadOnlyList<IssueReportDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<IssueReportDto>> GetOpenIssues(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetOpenIssuesQuery(id), cancellationToken);

    // Reports a problem with the resource, from the page its QR code opens (ADR-0037): anyone who
    // may use it, booked or not. A form with category, description, isBlocking and an optional
    // photo, a JPEG, PNG or WebP of at most 2 MB, as for a resource's photo.
    [HttpPost("{id:guid}/issues")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(ResourcePhotoRules.MaxRequestBytes)]
    [EnableRateLimiting(RateLimitingSetup.IssueReportPolicy)]
    [ProducesResponseType<IssueReportDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<IssueReportDto>> ReportIssue(
        Guid id, [FromForm] IssueReportForm form, CancellationToken cancellationToken)
    {
        byte[]? photo = null;
        if (form.Photo is { Length: > 0 } file)
        {
            using var data = new MemoryStream();
            await file.CopyToAsync(data, cancellationToken);
            photo = data.ToArray();
        }

        var issue = await sender.Send(
            new CreateIssueReportCommand(id, form.Category, form.Description ?? string.Empty, form.IsBlocking, photo),
            cancellationToken);
        return StatusCode(StatusCodes.Status201Created, issue);
    }
}

// The form a problem is reported with. Category is nullable so that a form without one is
// refused by the validator, in the caller's language, rather than read as the first category.
public sealed class IssueReportForm
{
    public IssueCategory? Category { get; init; }

    public string? Description { get; init; }

    public bool IsBlocking { get; init; }

    public IFormFile? Photo { get; init; }
}
