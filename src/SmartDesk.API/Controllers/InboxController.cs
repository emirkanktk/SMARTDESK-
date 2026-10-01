using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartDesk.Application.Features.Inbox.Commands.MarkAllRead;
using SmartDesk.Application.Features.Inbox.Commands.MarkRead;
using SmartDesk.Application.Features.Inbox.Common;
using SmartDesk.Application.Features.Inbox.Queries.GetInbox;
using SmartDesk.Application.Features.Inbox.Queries.GetUnreadCount;

namespace SmartDesk.API.Controllers;

// The signed-in user's own inbox (ADR-0032): messages about the meetings they're invited to.
// Nobody sees anyone else's.
[ApiController]
[Route("api/inbox")]
[Authorize]
public sealed class InboxController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<InboxMessageDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<InboxMessageDto>> List(CancellationToken cancellationToken) =>
        sender.Send(new GetInboxQuery(), cancellationToken);

    [HttpGet("unread-count")]
    [ProducesResponseType<UnreadInboxCountDto>(StatusCodes.Status200OK)]
    public Task<UnreadInboxCountDto> UnreadCount(CancellationToken cancellationToken) =>
        sender.Send(new GetUnreadInboxCountQuery(), cancellationToken);

    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new MarkInboxMessageReadCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        await sender.Send(new MarkAllInboxMessagesReadCommand(), cancellationToken);
        return NoContent();
    }
}
