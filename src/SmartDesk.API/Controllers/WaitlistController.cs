using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartDesk.Application.Features.Waitlist.Commands.JoinWaitlist;
using SmartDesk.Application.Features.Waitlist.Commands.LeaveWaitlist;
using SmartDesk.Application.Features.Waitlist.Common;
using SmartDesk.Application.Features.Waitlist.Queries.GetMyWaitlist;

namespace SmartDesk.API.Controllers;

// The caller's waiting list (ADR-0043): times they wait for on resources that are taken then,
// booked for them, in the order people joined, when they free up.
[ApiController]
[Route("api/waitlist")]
[Authorize]
public sealed class WaitlistController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<WaitlistEntryDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<WaitlistEntryDto>> GetMine(CancellationToken cancellationToken) =>
        sender.Send(new GetMyWaitlistQuery(), cancellationToken);

    [HttpPost]
    [ProducesResponseType<WaitlistEntryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Join(JoinWaitlistCommand command, CancellationToken cancellationToken)
    {
        var entry = await sender.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetMine), null, entry);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Leave(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new LeaveWaitlistCommand(id), cancellationToken);
        return NoContent();
    }
}
