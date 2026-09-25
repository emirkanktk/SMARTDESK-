namespace SmartDesk.Application.Features.ResourceBlocks.Common;

public static class ResourceBlockRules
{
    // Long enough for a renovation or a car's service contract, short enough to catch a typo
    // in the year.
    public static readonly TimeSpan MaxDuration = TimeSpan.FromDays(366);

    public const int MaxReasonLength = 200;
}
