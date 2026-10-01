namespace SmartDesk.Application.Features.Reservations.Common;

// Running over (ADR-0033): shortly before a booking ends, its owner is asked whether they need
// longer, and adds one of these with a click instead of picking a new end.
public static class ExtensionRules
{
    public static readonly IReadOnlyList<int> Options = [15, 30, 60];

    public static int MaxMinutes => Options[^1];

    // How long before the end the owner is asked.
    public static readonly TimeSpan NoticeBefore = TimeSpan.FromMinutes(10);

    public static bool IsOption(int minutes) => Options.Contains(minutes);
}
