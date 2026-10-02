using MediatR;
using SmartDesk.Application.Features.Meetings.Common;

namespace SmartDesk.Application.Features.Meetings.Queries.GetInvitees;

// The people the caller may invite to a booking (ADR-0032), for the booking form's list:
// a Manager's own team, everyone for an Admin, nobody for an Employee.
public sealed record GetInviteesQuery : IRequest<IReadOnlyList<InviteeDto>>;

public sealed record InviteeDto(Guid Id, string Name, string Email);

public sealed class GetInviteesQueryHandler(Invitees invitees) : IRequestHandler<GetInviteesQuery, IReadOnlyList<InviteeDto>>
{
    public async Task<IReadOnlyList<InviteeDto>> Handle(GetInviteesQuery request, CancellationToken cancellationToken) =>
        (await invitees.ListAsync(cancellationToken))
            .Select(person => new InviteeDto(person.Id, person.DisplayName, person.Email))
            .ToList();
}
