namespace SmartDesk.Application.Features.Reports.Common;

// ADR-0017: what the usage report measures. The hours it measures against are the office's
// (ADR-0046).
public static class ReportRules
{
    // A quarter: enough for a quarterly review, small enough to compute on each request.
    public const int MaxRangeDays = 92;

    public const int TopUsers = 5;
}
