namespace SmartDesk.Application.Common.Exceptions;

// The request is well-formed but clashes with the current state of the system - an
// overlapping reservation, a resource that isn't bookable, a reservation that has already
// ended. Mapped to 409 by the API; the message is meant to be shown to the user as-is.
public sealed class ConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    // A stable name for the kind of conflict (ConflictCodes), sent as the problem's `code` for
    // clients that have to tell conflicts apart. The message depends on the language; this
    // doesn't (ADR-0023).
    public string? Code { get; init; }
}
