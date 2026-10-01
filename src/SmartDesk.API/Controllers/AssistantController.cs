using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartDesk.API.RateLimiting;
using SmartDesk.Application.Features.Assistant.Commands.AskAssistant;
using SmartDesk.Application.Features.Assistant.Queries.GetAssistantStatus;

namespace SmartDesk.API.Controllers;

// The assistant (ADR-0029): it looks things up as the caller and proposes bookings and
// cancellations; the caller confirms them through the reservation endpoints.
[ApiController]
[Route("api/assistant")]
[Authorize]
public sealed class AssistantController(ISender sender) : ControllerBase
{
    // Whether this server has an assistant; the app shows it only then.
    [HttpGet]
    [ProducesResponseType<AssistantStatusDto>(StatusCodes.Status200OK)]
    public Task<AssistantStatusDto> Status(CancellationToken cancellationToken) =>
        sender.Send(new GetAssistantStatusQuery(), cancellationToken);

    // The conversation so far, ending with the user's message; the answer and any cards.
    [HttpPost("messages")]
    [EnableRateLimiting(RateLimitingSetup.AssistantPolicy)]
    [ProducesResponseType<AssistantReplyDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<AssistantReplyDto> Ask(AskAssistantCommand command, CancellationToken cancellationToken) =>
        sender.Send(command, cancellationToken);
}
