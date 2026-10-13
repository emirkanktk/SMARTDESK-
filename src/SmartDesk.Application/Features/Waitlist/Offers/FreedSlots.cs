using MediatR;

namespace SmartDesk.Application.Features.Waitlist.Offers;

// A time a resource got back (ADR-0043): a booking cancelled, released for want of a check-in,
// ended early, shortened or moved, or a block lifted. Published once the request that freed it
// has finished, for the waiting list.
public sealed record SlotFreed(Guid ResourceId, DateTime From, DateTime To) : INotification;

// The times freed during one request, as its saves noted them (Infrastructure's
// FreedSlotsInterceptor), until OfferFreedSlotsBehavior publishes them. One per scope.
public sealed class FreedSlots
{
    private readonly List<SlotFreed> _slots = [];

    public void Add(Guid resourceId, DateTime from, DateTime to)
    {
        if (to > from)
        {
            _slots.Add(new SlotFreed(resourceId, from, to));
        }
    }

    // All noted so far, which are then forgotten.
    public IReadOnlyList<SlotFreed> TakeAll()
    {
        var slots = _slots.ToList();
        _slots.Clear();
        return slots;
    }

    public void Clear() => _slots.Clear();
}
