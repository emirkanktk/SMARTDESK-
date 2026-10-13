using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.BookingRules.Common;

namespace SmartDesk.Application.Features.BookingRules.Queries.GetBookingRules;

// The rules bookings keep to (ADR-0046), for anyone signed in: the booking form shows them and
// keeps to them before it asks, and an Admin changes them.
public sealed record GetBookingRulesQuery : IRequest<BookingRulesDto>;

public sealed class GetBookingRulesQueryHandler(IApplicationDbContext db) : IRequestHandler<GetBookingRulesQuery, BookingRulesDto>
{
    public async Task<BookingRulesDto> Handle(GetBookingRulesQuery request, CancellationToken cancellationToken)
    {
        var office = await db.OfficeHours.AsNoTracking().SingleAsync(cancellationToken);
        var types = await db.ResourceTypes.AsNoTracking().OrderBy(t => t.Name).ToListAsync(cancellationToken);

        return new BookingRulesDto(OfficeHoursDto.From(office), types.Select(ResourceTypeRulesDto.From).ToList(), RuleBoundsDto.Current);
    }
}
