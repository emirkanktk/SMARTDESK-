using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Sessions.Commands.EndOtherSessions;
using SmartDesk.Application.Features.Sessions.Commands.EndSession;
using SmartDesk.Application.Features.Sessions.Queries.GetMySessions;

namespace SmartDesk.API.Controllers;

// The devices the caller is signed in on (ADR-0061): each sign-in session with its browser, how
// it began and when it was last used, and ending one or all the others from here. Only ever the
// caller's own.
[ApiController]
[Route("api/auth/sessions")]
[Authorize]
public sealed class SessionsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SignInSessionDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<SignInSessionDto>> List(CancellationToken cancellationToken) =>
        sender.Send(new GetMySessionsQuery(), cancellationToken);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> End(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new EndSessionCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("end-others")]
    [ProducesResponseType<EndedSessionsDto>(StatusCodes.Status200OK)]
    public Task<EndedSessionsDto> EndOthers(CancellationToken cancellationToken) =>
        sender.Send(new EndOtherSessionsCommand(), cancellationToken);
}
