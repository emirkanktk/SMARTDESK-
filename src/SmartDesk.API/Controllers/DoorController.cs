using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartDesk.API.RateLimiting;
using SmartDesk.Application.Features.CheckIn.Commands.CheckIn;
using SmartDesk.Application.Features.CheckIn.Queries.GetDoorScreen;
using SmartDesk.Application.Features.CheckIn.Queries.GetDoorVisit;
using SmartDesk.Application.Features.Issues.Common;
using SmartDesk.Application.Features.Resources.Common;

namespace SmartDesk.API.Controllers;

// For what has no user at a resource's door. Its screen (ADR-0034) proves itself with its own
// token; the page its QR code opens for someone without an account (ADR-0050), with the code the
// page was opened with. Both in a header when they only read, so they stay out of addresses and
// logs.
[ApiController]
[Route("api/door")]
[AllowAnonymous]
public sealed class DoorController(ISender sender) : ControllerBase
{
    public const string TokenHeader = "X-Door-Token";
    public const string CodeHeader = "X-Check-In-Code";

    [HttpGet("{resourceId:guid}")]
    [EnableRateLimiting(RateLimitingSetup.DoorScreenPolicy)]
    [ProducesResponseType<DoorScreenDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public Task<DoorScreenDto> Get(
        Guid resourceId,
        [FromHeader(Name = TokenHeader)] string? token,
        [FromQuery] string? timeZone,
        CancellationToken cancellationToken) =>
        sender.Send(new GetDoorScreenQuery(resourceId, token, timeZone), cancellationToken);

    // The resource's page, for whoever opened its QR code without an account.
    [HttpGet("{resourceId:guid}/visit")]
    [EnableRateLimiting(RateLimitingSetup.DoorPagePolicy)]
    [ProducesResponseType<DoorVisitDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public Task<DoorVisitDto> Visit(
        Guid resourceId,
        [FromHeader(Name = CodeHeader)] string? code,
        [FromQuery] string? timeZone,
        CancellationToken cancellationToken) =>
        sender.Send(new GetDoorVisitQuery(resourceId, code, timeZone), cancellationToken);

    // Checks in the booking of the resource waiting for it now, whoever's it is: the code shows
    // someone is at the door. With a door screen, only its current code does (ADR-0034). The
    // answer is the booking's times, with nobody's name.
    [HttpPost("{resourceId:guid}/check-in")]
    [EnableRateLimiting(RateLimitingSetup.CheckInPolicy)]
    [ProducesResponseType<DoorBookingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<DoorBookingDto> CheckIn(Guid resourceId, CheckInCommand command, CancellationToken cancellationToken)
    {
        var reservation = await sender.Send(command with { ResourceId = resourceId, AtTheDoor = true }, cancellationToken);
        return DoorBookingDto.CheckedInAtTheDoor(reservation);
    }

    // Reports a problem with the resource, as its signed-in page does (ADR-0037), from a form
    // with the code the page was opened with.
    [HttpPost("{resourceId:guid}/issues")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(ResourcePhotoRules.MaxRequestBytes)]
    [EnableRateLimiting(RateLimitingSetup.IssueReportPolicy)]
    [ProducesResponseType<IssueReportDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<IssueReportDto>> ReportIssue(
        Guid resourceId, [FromForm] DoorIssueReportForm form, CancellationToken cancellationToken)
    {
        var command = form.ToCommand(resourceId, await form.ReadPhotoAsync(cancellationToken)) with
        {
            DoorCode = form.Code ?? string.Empty,
        };
        var issue = await sender.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, issue);
    }
}
