using MediatR;
using SmartDesk.Application.Common.Exceptions;

namespace SmartDesk.Application.Features.Reservations.Common;

// A booking or a move that can't be made - the time is taken or blocked, or the resource is
// out of use - comes back as the same 409 with what would do instead (ADR-0042). Worked out
// once the handler has let go of its locks, from what's committed by then.
public sealed class BookingSuggestionsBehavior<TRequest>(BookingSuggester suggester)
    : IPipelineBehavior<TRequest, ReservationDto>
    where TRequest : IBookingRequest, IRequest<ReservationDto>
{
    public async Task<ReservationDto> Handle(
        TRequest request,
        RequestHandlerDelegate<ReservationDto> next,
        CancellationToken cancellationToken)
    {
        try
        {
            return await next(cancellationToken);
        }
        catch (ConflictException conflict) when (conflict.Suggestions is null && Suggestible(conflict.Code))
        {
            var suggestions = await suggester.SuggestAsync(
                request, sameResource: conflict.Code != ConflictCodes.ResourceUnavailable, cancellationToken);
            throw new ConflictException(conflict.Message, conflict) { Code = conflict.Code, Suggestions = suggestions };
        }
    }

    private static bool Suggestible(string? code) =>
        code is ConflictCodes.ReservationOverlaps or ConflictCodes.ResourceBlocked or ConflictCodes.ResourceUnavailable;
}
