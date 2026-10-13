using MediatR;

namespace SmartDesk.Application.Features.Waitlist.Offers;

// After any request, the times it freed go to the waiting list (ADR-0043), as SlotFreed
// notifications: only once it has succeeded, so everything it saved is committed, and outside
// its transactions and locks. A request that fails offers nothing, since what it saved may
// have rolled back.
public sealed class OfferFreedSlotsBehavior<TRequest, TResponse>(FreedSlots freed, IPublisher publisher)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        TResponse response;
        try
        {
            response = await next(cancellationToken);
        }
        catch
        {
            freed.Clear();
            throw;
        }

        foreach (var slot in freed.TakeAll())
        {
            await publisher.Publish(slot, cancellationToken);
        }

        return response;
    }
}
