using MediatR;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.CheckIn.Queries.GetCheckInTimes;

// The check-in window's lengths, in minutes (ADR-0040), so the app says and counts with the
// ones the API uses: the grace is a setting. Nobody's data, so anyone may ask, a door screen too.
public sealed record GetCheckInTimesQuery : IRequest<CheckInTimesDto>;

// From OpensBeforeMinutes before a booking's start until GraceMinutes after; everyone on it is
// warned WarnBeforeReleaseMinutes before it's released.
public sealed record CheckInTimesDto(int OpensBeforeMinutes, int GraceMinutes, int WarnBeforeReleaseMinutes);

public sealed class GetCheckInTimesQueryHandler(ICheckInTimes times) : IRequestHandler<GetCheckInTimesQuery, CheckInTimesDto>
{
    public Task<CheckInTimesDto> Handle(GetCheckInTimesQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(new CheckInTimesDto(
            (int)CheckInRules.OpensBefore.TotalMinutes,
            (int)times.Grace.TotalMinutes,
            (int)CheckInRules.WarningLead(times.Grace).TotalMinutes));
}
