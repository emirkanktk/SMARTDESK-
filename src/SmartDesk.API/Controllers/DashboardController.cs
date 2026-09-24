using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartDesk.Application.Features.Dashboard.Queries.GetDashboard;

namespace SmartDesk.API.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public sealed class DashboardController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<DashboardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<DashboardDto> Get([FromQuery] string? timeZone, CancellationToken cancellationToken) =>
        sender.Send(new GetDashboardQuery(timeZone), cancellationToken);
}
