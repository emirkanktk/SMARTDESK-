using Microsoft.Extensions.Options;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Infrastructure;

// CheckIn:GraceMinutes - how long after its start a booking that needs a check-in waits for one
// before it's released (ADR-0030, ADR-0040): 15 by default, from 1 to 60. A demo can set 1 to
// show a release in a minute. CheckIn:IntervalSeconds and CheckIn:Enabled, for the job that
// releases, are read by the API's worker.
public sealed class CheckInOptions
{
    public const string SectionName = "CheckIn";

    public int GraceMinutes { get; init; } = (int)CheckInRules.DefaultGrace.TotalMinutes;

    public bool HasValidGrace =>
        GraceMinutes >= CheckInRules.MinGrace.TotalMinutes && GraceMinutes <= CheckInRules.MaxGrace.TotalMinutes;
}

public sealed class CheckInTimes(IOptions<CheckInOptions> options) : ICheckInTimes
{
    public TimeSpan Grace => TimeSpan.FromMinutes(options.Value.GraceMinutes);
}
