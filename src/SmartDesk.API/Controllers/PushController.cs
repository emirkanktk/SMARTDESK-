using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartDesk.API.RateLimiting;
using SmartDesk.Application.Features.Push.Commands.DeletePushSubscription;
using SmartDesk.Application.Features.Push.Commands.SavePushSubscription;
using SmartDesk.Application.Features.Push.Commands.SendTestPush;
using SmartDesk.Application.Features.Push.Queries.GetPushStatus;

namespace SmartDesk.API.Controllers;

// Notifications on the signed-in user's phones and browsers (ADR-0056). A device subscribes with the
// browser's Push API, using the key GET gives, and hands over the subscription's address and keys;
// the server then sends through that device's push service. Saving and removing are idempotent.
[ApiController]
[Route("api/push")]
[Authorize]
public sealed class PushController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PushStatusDto>(StatusCodes.Status200OK)]
    public Task<PushStatusDto> Status(CancellationToken cancellationToken) =>
        sender.Send(new GetPushStatusQuery(), cancellationToken);

    [HttpPut("subscription")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Save(SavePushSubscriptionCommand command, CancellationToken cancellationToken)
    {
        await sender.Send(command, cancellationToken);
        return NoContent();
    }

    [HttpDelete("subscription")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(DeletePushSubscriptionCommand command, CancellationToken cancellationToken)
    {
        await sender.Send(command, cancellationToken);
        return NoContent();
    }

    // A notification to every device the caller has them on; the answer says how many.
    [HttpPost("test")]
    [EnableRateLimiting(RateLimitingSetup.PushTestPolicy)]
    [ProducesResponseType<PushTestDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Test(CancellationToken cancellationToken) =>
        Accepted(await sender.Send(new SendTestPushCommand(), cancellationToken));
}
