using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using SmartDesk.Application.Features.Resources.Commands.ChangeResourceStatus;
using SmartDesk.Application.Features.Resources.Commands.CreateResource;
using SmartDesk.Application.Features.Resources.Commands.RemoveResourcePhoto;
using SmartDesk.Application.Features.Resources.Commands.SetResourcePhoto;
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
}
