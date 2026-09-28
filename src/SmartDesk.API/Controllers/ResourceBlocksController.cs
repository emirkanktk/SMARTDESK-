using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartDesk.Application.Features.ResourceBlocks.Commands.CreateResourceBlock;
using SmartDesk.Application.Features.ResourceBlocks.Commands.DeleteResourceBlock;
using SmartDesk.Application.Features.ResourceBlocks.Common;
using SmartDesk.Application.Features.ResourceBlocks.Queries.GetResourceBlocks;
using SmartDesk.Domain.Constants;

namespace SmartDesk.API.Controllers;

[ApiController]
[Route("api/resource-blocks")]
[Authorize]
public sealed class ResourceBlocksController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ResourceBlockDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<IReadOnlyList<ResourceBlockDto>> GetInRange(
        [FromQuery] GetResourceBlocksQuery query,
        CancellationToken cancellationToken) =>
        sender.Send(query, cancellationToken);

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ResourceBlockDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ResourceBlockDto>> Create(CreateResourceBlockCommand command, CancellationToken cancellationToken)
    {
        var block = await sender.Send(command, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, block);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteResourceBlockCommand(id), cancellationToken);

        return NoContent();
    }
}
