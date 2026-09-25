using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartDesk.Application.Features.Reports.Queries.GetUsageReport;
using SmartDesk.Domain.Constants;

namespace SmartDesk.API.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize(Roles = Roles.Admin)]
public sealed class ReportsController(ISender sender) : ControllerBase
{
    [HttpGet("usage")]
    [ProducesResponseType<UsageReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<UsageReportDto> Usage([FromQuery] GetUsageReportQuery query, CancellationToken cancellationToken) =>
        sender.Send(query, cancellationToken);
}
