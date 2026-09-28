using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Accounts.Commands.AddTeamMember;
using SmartDesk.Application.Features.Accounts.Commands.RemoveTeamMember;
using SmartDesk.Application.Features.Accounts.Commands.SendPasswordReset;
using SmartDesk.Application.Features.Accounts.Commands.SetAccountStatus;
using SmartDesk.Application.Features.Accounts.Queries.GetMyTeam;
using SmartDesk.Domain.Constants;

namespace SmartDesk.API.Controllers;

// A Manager's own team (ADR-0020). Anyone outside it answers 404.
[ApiController]
[Route("api/team")]
[Authorize(Roles = Roles.Manager)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class TeamController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<UserAccount>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<UserAccount>> GetMembers(CancellationToken cancellationToken) =>
        sender.Send(new GetMyTeamQuery(), cancellationToken);

    // The new member is emailed a link to choose a password.
    [HttpPost("members")]
    [ProducesResponseType<UserAccount>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserAccount>> AddMember(AddTeamMemberCommand command, CancellationToken cancellationToken)
    {
        var member = await sender.Send(command, cancellationToken);
        return Created($"/api/team/members/{member.Id}", member);
    }

    // Takes them out of the team; the account itself stays.
    [HttpDelete("members/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveMember(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new RemoveTeamMemberCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("members/{id:guid}/disable")]
    [ProducesResponseType<UserAccount>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<UserAccount> DisableMember(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new SetAccountStatusCommand(id, Enabled: false), cancellationToken);

    [HttpPost("members/{id:guid}/enable")]
    [ProducesResponseType<UserAccount>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<UserAccount> EnableMember(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new SetAccountStatusCommand(id, Enabled: true), cancellationToken);

    [HttpPost("members/{id:guid}/password-reset")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SendMemberPasswordReset(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SendPasswordResetCommand(id), cancellationToken);
        return Accepted();
    }
}
