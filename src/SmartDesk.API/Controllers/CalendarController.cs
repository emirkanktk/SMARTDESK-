using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartDesk.API.RateLimiting;
using SmartDesk.Application.Features.Calendar.Commands.CreateCalendarSubscription;
using SmartDesk.Application.Features.Calendar.Commands.DeleteCalendarSubscription;
using SmartDesk.Application.Features.Calendar.Common;
using SmartDesk.Application.Features.Calendar.Queries.GetCalendarFeed;
using SmartDesk.Application.Features.Calendar.Queries.GetCalendarSubscription;

namespace SmartDesk.API.Controllers;

// Personal calendar links (ADR-0048): the signed-in user makes, checks and removes their own,
// and their calendar app fetches it without a sign-in.
[ApiController]
[Route("api/calendar")]
[Authorize]
public sealed class CalendarController(ISender sender) : ControllerBase
{
    public const string ContentType = "text/calendar; charset=utf-8";

    // Whether the caller has a link, since when, and when a calendar app last fetched it. 204
    // when they have none. Never the link itself: only its hash is kept.
    [HttpGet("subscription")]
    [ProducesResponseType<CalendarSubscriptionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public Task<CalendarSubscriptionDto?> GetSubscription(CancellationToken cancellationToken) =>
        sender.Send(new GetCalendarSubscriptionQuery(), cancellationToken);

    // A new link, in this answer only. It replaces the caller's old one, which stops working.
    [HttpPost("subscription")]
    [ProducesResponseType<CalendarSubscriptionDto>(StatusCodes.Status200OK)]
    public Task<CalendarSubscriptionDto> CreateSubscription(CancellationToken cancellationToken) =>
        sender.Send(new CreateCalendarSubscriptionCommand(), cancellationToken);

    [HttpDelete("subscription")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteSubscription(CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteCalendarSubscriptionCommand(), cancellationToken);
        return NoContent();
    }

    // The feed a calendar app subscribes to. A calendar app can't sign in or send a header of
    // ours, so the link's token is the proof, in the query string; limited by address, as the
    // door screen is. A token that isn't a link's gets a plain 404.
    [HttpGet("feed.ics")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingSetup.CalendarFeedPolicy)]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Feed(
        [FromQuery] string? token, [FromServices] ICalendarWriter writer, CancellationToken cancellationToken)
    {
        var calendar = await sender.Send(new GetCalendarFeedQuery(token), cancellationToken);
        if (calendar is null)
        {
            return NotFound();
        }

        // Someone's bookings: for them, not for a cache on the way.
        Response.Headers.CacheControl = "private, no-store";
        return File(writer.Write(calendar), ContentType);
    }
}
