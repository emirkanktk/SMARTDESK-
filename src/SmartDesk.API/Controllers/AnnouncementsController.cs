using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartDesk.Application.Features.Announcements.Commands.CreateAnnouncement;
using SmartDesk.Application.Features.Announcements.Commands.DeleteAnnouncement;
using SmartDesk.Application.Features.Announcements.Common;
using SmartDesk.Application.Features.Announcements.Queries.GetAnnouncements;
using SmartDesk.Domain.Constants;

namespace SmartDesk.API.Controllers;

// The office's announcements (ADR-0065): everyone reads what's showing; Admins post and take
// them down.
[ApiController]
[Route("api/announcements")]
[Authorize]
public sealed class AnnouncementsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AnnouncementDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<AnnouncementDto>> Current(CancellationToken cancellationToken) =>
        sender.Send(new GetCurrentAnnouncementsQuery(), cancellationToken);

    [HttpGet("all")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<IReadOnlyList<AnnouncementDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<AnnouncementDto>> All(CancellationToken cancellationToken) =>
        sender.Send(new GetAllAnnouncementsQuery(), cancellationToken);

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<AnnouncementDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AnnouncementDto>> Create(CreateAnnouncementCommand command, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await sender.Send(command, cancellationToken));

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteAnnouncementCommand(id), cancellationToken);
        return NoContent();
    }
}
