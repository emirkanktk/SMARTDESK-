using Microsoft.Extensions.Options;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Infrastructure;

namespace SmartDesk.Tests.TestSupport;

// The check-in grace for handler tests (ADR-0040): the default quarter of an hour, unless a
// test sets another, as the setting would.
public static class TestCheckInTimes
{
    public static ICheckInTimes Default { get; } = WithGrace(new CheckInOptions().GraceMinutes);

    public static ICheckInTimes WithGrace(int minutes) =>
        new CheckInTimes(Options.Create(new CheckInOptions { GraceMinutes = minutes }));
}
