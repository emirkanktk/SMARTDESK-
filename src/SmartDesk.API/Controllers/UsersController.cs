using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Accounts.Commands.ApproveAccount;
using SmartDesk.Application.Features.Accounts.Commands.AssignManager;
using SmartDesk.Application.Features.Accounts.Commands.ChangeAccountRole;
using SmartDesk.Application.Features.Accounts.Commands.CreateAccount;
using SmartDesk.Application.Features.Accounts.Commands.RejectAccount;
using SmartDesk.Application.Features.Accounts.Commands.SendPasswordReset;
using SmartDesk.Application.Features.Accounts.Commands.SetAccountStatus;
using SmartDesk.Application.Features.Accounts.Queries.GetAccounts;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Enums;

namespace SmartDesk.API.Controllers;

// Every account, for Admins (ADR-0020).
[ApiController]
[Route("api/users")]
[Authorize(Roles = Roles.Admin)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class UsersController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<UserAccount>>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<IReadOnlyList<UserAccount>> GetAll(
        [FromQuery] AccountStatus? status,
        [FromQuery] string? role,
        [FromQuery] string? search,
        CancellationToken cancellationToken) =>
        sender.Send(new GetAccountsQuery(status, role, search), cancellationToken);

    // The new account's owner is emailed a link to choose a password.
    [HttpPost]
    [ProducesResponseType<UserAccount>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserAccount>> Create(CreateAccountCommand command, CancellationToken cancellationToken)
    {
        var account = await sender.Send(command, cancellationToken);
        return Created($"/api/users/{account.Id}", account);
    }

    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType<UserAccount>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<UserAccount> Approve(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new ApproveAccountCommand(id), cancellationToken);

    // Deletes the waiting sign-up.
    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new RejectAccountCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPatch("{id:guid}/role")]
    [ProducesResponseType<UserAccount>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<UserAccount> ChangeRole(Guid id, ChangeAccountRoleCommand command, CancellationToken cancellationToken) =>
        sender.Send(command with { Id = id }, cancellationToken);

    [HttpPatch("{id:guid}/manager")]
    [ProducesResponseType<UserAccount>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<UserAccount> AssignManager(Guid id, AssignManagerCommand command, CancellationToken cancellationToken) =>
        sender.Send(command with { Id = id }, cancellationToken);

    [HttpPost("{id:guid}/disable")]
    [ProducesResponseType<UserAccount>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<UserAccount> Disable(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new SetAccountStatusCommand(id, Enabled: false), cancellationToken);

    [HttpPost("{id:guid}/enable")]
    [ProducesResponseType<UserAccount>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<UserAccount> Enable(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new SetAccountStatusCommand(id, Enabled: true), cancellationToken);

    // Emails the owner a reset link; nobody but them sees or sets the password.
    [HttpPost("{id:guid}/password-reset")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SendPasswordReset(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new SendPasswordResetCommand(id), cancellationToken);
        return Accepted();
    }
}
