using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartDesk.Application.Features.Reports.Common;
using SmartDesk.Application.Features.Reports.Queries.GetUsageReport;
using SmartDesk.Domain.Constants;

namespace SmartDesk.API.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize(Roles = Roles.Admin)]
public sealed class ReportsController(ISender sender, IUsageReportWorkbook workbook) : ControllerBase
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [HttpGet("usage")]
    [ProducesResponseType<UsageReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<UsageReportDto> Usage([FromQuery] GetUsageReportQuery query, CancellationToken cancellationToken) =>
        sender.Send(query, cancellationToken);

    // The same report as an Excel workbook (ADR-0045), in the request's language.
    [HttpGet("usage/workbook")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, Xlsx)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UsageWorkbook([FromQuery] GetUsageReportQuery query, CancellationToken cancellationToken)
    {
        var report = await sender.Send(query, cancellationToken);
        return File(workbook.Write(report), Xlsx, $"smartdesk-usage-{report.From:yyyy-MM-dd}-{report.To:yyyy-MM-dd}.xlsx");
    }
}
