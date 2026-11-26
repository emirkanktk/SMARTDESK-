using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartDesk.Application.Features.LobbyScreens.Commands.CreateLobbyScreen;
using SmartDesk.Application.Features.LobbyScreens.Commands.DeleteLobbyScreen;
using SmartDesk.Application.Features.LobbyScreens.Common;
using SmartDesk.Application.Features.LobbyScreens.Queries.GetLobbyScreens;
using SmartDesk.Domain.Constants;

namespace SmartDesk.API.Controllers;

// The office's lobby screens (ADR-0064), set up and taken down by its Admins.
[ApiController]
[Route("api/lobby-screens")]
[Authorize(Roles = Roles.Admin)]
public sealed class LobbyScreensController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<LobbyScreenDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<LobbyScreenDto>> List(CancellationToken cancellationToken) =>
        sender.Send(new GetLobbyScreensQuery(), cancellationToken);

    // The answer carries the link to open on the screen, shown this once.
    [HttpPost]
    [ProducesResponseType<NewLobbyScreenDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<NewLobbyScreenDto>> Create(CreateLobbyScreenCommand command, CancellationToken cancellationToken)
    {
        var created = await sender.Send(command, cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteLobbyScreenCommand(id), cancellationToken);
        return NoContent();
    }
}
