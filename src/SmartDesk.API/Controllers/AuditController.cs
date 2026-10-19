using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartDesk.Application.Features.Audit.Queries.GetAuditEntries;
using SmartDesk.Domain.Constants;

namespace SmartDesk.API.Controllers;

// The history of changes (ADR-0052), for Admins: who changed what and when, written by the saves
// themselves. Read only: nothing in it is ever changed.
[ApiController]
[Route("api/audit")]
[Authorize(Roles = Roles.Admin)]
public sealed class AuditController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<AuditPageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<AuditPageDto> Get(
        [FromQuery] string? area,
        [FromQuery] Guid? resourceId,
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] Guid? userId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] long? before,
        CancellationToken cancellationToken,
        [FromQuery] int take = GetAuditEntriesQueryHandler.DefaultTake) =>
        sender.Send(
            new GetAuditEntriesQuery(area, resourceId, entityType, entityId, userId, from, to, before, take),
            cancellationToken);
}
