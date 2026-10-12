namespace SmartDesk.Application.Features.Reservations.Common;

// The free times nearest to a wanted one on a resource that's taken then (ADR-0042): one in
// each gap between its bookings and blocks, as near the wanted start as the gap allows, on the
// quarter hour the booking form works in. Pure, so the rules are easy to test.
public static class FreeTimes
{
    public static readonly TimeSpan Step = TimeSpan.FromMinutes(15);

    // `busy`: the bookings and blocks seen in [from, to), the stretch looked at - nothing is
    // known outside it. `earliest` and `latest`: the first and last start a booking may have.
    // At most `count` times, earliest first.
    public static IReadOnlyList<(DateTime Start, DateTime End)> Nearest(
        IEnumerable<(DateTime Start, DateTime End)> busy,
        DateTime wantedStart,
        TimeSpan duration,
        DateTime from,
        DateTime to,
        DateTime earliest,
        DateTime latest,
        int count)
    {
        var starts = new List<DateTime>();
        foreach (var (gapStart, gapEnd) in Gaps(busy, from, to))
        {
            var low = Ceiling(Max(gapStart, earliest));
            var high = Floor(Min(gapEnd - duration, latest));
            if (low > high)
            {
                continue;
            }

            starts.Add(wantedStart < low ? low : wantedStart > high ? high : Floor(wantedStart));
        }

        return starts
            .OrderBy(start => (start - wantedStart).Duration())
            // Equally near: the later one first.
            .ThenByDescending(start => start)
            .Take(count)
            .Order()
            .Select(start => (start, start + duration))
            .ToList();
    }

    // The stretches of [from, to) that nothing covers.
    private static IEnumerable<(DateTime Start, DateTime End)> Gaps(
        IEnumerable<(DateTime Start, DateTime End)> busy, DateTime from, DateTime to)
    {
        var cursor = from;
        foreach (var (start, end) in busy.Where(b => b.End > from && b.Start < to).OrderBy(b => b.Start))
        {
            if (start > cursor)
            {
                yield return (cursor, start);
            }

            cursor = Max(cursor, end);
        }

        if (cursor < to)
        {
            yield return (cursor, to);
        }
    }

    private static DateTime Floor(DateTime time) => new(time.Ticks - (time.Ticks % Step.Ticks), time.Kind);

    private static DateTime Ceiling(DateTime time) =>
        time.Ticks % Step.Ticks == 0 ? time : Floor(time) + Step;

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
