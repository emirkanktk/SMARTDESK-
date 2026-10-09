namespace SmartDesk.Application.Common.Interfaces;

// How long a booking that needs a check-in waits for one after its start before it's released:
// configuration (CheckIn:GraceMinutes, 15 by default, ADR-0040), so a demo can show a release
// in a minute.
public interface ICheckInTimes
{
    TimeSpan Grace { get; }
}
