using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using SmartDesk.Application.Features.FloorPlans.Commands.RemoveFloorPlanImage;
using SmartDesk.Application.Features.FloorPlans.Commands.SetFloorPlanImage;
using SmartDesk.Application.Features.FloorPlans.Common;
using SmartDesk.Application.Features.FloorPlans.Queries.GetFloorPlanImage;
using SmartDesk.Application.Features.FloorPlans.Queries.GetFloors;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Domain.Constants;

namespace SmartDesk.API.Controllers;

// The floors and their plans (ADR-0041). A floor is a location the resources share, so it
// travels in the query string: "?location=5th%20Floor".
[ApiController]
[Route("api/floor-plans")]
[Authorize]
public sealed class FloorPlansController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<FloorDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<FloorDto>> GetAll(CancellationToken cancellationToken) =>
        sender.Send(new GetFloorsQuery(), cancellationToken);

    // Like a resource's photo: kept by the browser, asked again with the ETag (ADR-0027).
    [HttpGet("image")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "image/jpeg", "image/png", "image/webp")]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetImage([FromQuery] string? location, CancellationToken cancellationToken)
    {
        var plan = await sender.Send(new GetFloorPlanImageQuery(location ?? string.Empty), cancellationToken);

        Response.Headers.CacheControl = "private, no-cache";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(plan.Data, plan.ContentType, plan.UpdatedAt, new EntityTagHeaderValue($"\"{plan.UpdatedAt.Ticks:x}\""));
    }

    // A JPEG, PNG or WebP of at most 2 MB and 4096 pixels a side, in the form field "plan".
    [HttpPut("image")]
    [Authorize(Roles = Roles.Admin)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(ResourcePhotoRules.MaxRequestBytes)]
    [ProducesResponseType<FloorDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<FloorDto> SetImage([FromQuery] string? location, IFormFile? plan, CancellationToken cancellationToken)
    {
        using var data = new MemoryStream();
        if (plan is not null)
        {
            await plan.CopyToAsync(data, cancellationToken);
        }

        return await sender.Send(new SetFloorPlanImageCommand(location ?? string.Empty, data.ToArray()), cancellationToken);
    }

    // The resources keep their places, for the next plan. Removing none is fine.
    [HttpDelete("image")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RemoveImage([FromQuery] string? location, CancellationToken cancellationToken)
    {
        await sender.Send(new RemoveFloorPlanImageCommand(location ?? string.Empty), cancellationToken);
        return NoContent();
    }
}
