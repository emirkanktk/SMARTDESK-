using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartDesk.Application.Features.CheckIn.Queries.GetCheckInTimes;

namespace SmartDesk.API.Controllers;

// The check-in window's lengths (ADR-0040), read by the app once as it starts. Anonymous: they
// say nothing about anyone, and a door screen has no user.
[ApiController]
[Route("api/check-in")]
[AllowAnonymous]
public sealed class CheckInController(ISender sender) : ControllerBase
{
    [HttpGet("times")]
    [ProducesResponseType<CheckInTimesDto>(StatusCodes.Status200OK)]
    public Task<CheckInTimesDto> GetTimes(CancellationToken cancellationToken) =>
        sender.Send(new GetCheckInTimesQuery(), cancellationToken);
}
