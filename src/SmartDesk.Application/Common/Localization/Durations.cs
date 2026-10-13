using SmartDesk.Application.Resources;

namespace SmartDesk.Application.Common.Localization;

// A length of time as a message says it, in the request's language: "9 hours", "1 day 4 hours",
// "1 saat 30 dakika". Whole minutes; a part that's zero is left out.
public static class Durations
{
    public static string Describe(TimeSpan span)
    {
        var parts = new List<string>();
        if (span.Days > 0)
        {
            parts.Add(Days(span.Days));
        }

        if (span.Hours > 0)
        {
            parts.Add(span.Hours == 1 ? Messages.Duration_Hour : string.Format(Messages.Duration_Hours, span.Hours));
        }

        if (span.Minutes > 0 || parts.Count == 0)
        {
            parts.Add(string.Format(Messages.Duration_Minutes, span.Minutes));
        }

        return string.Join(" ", parts);
    }

    public static string Days(int days) => days == 1 ? Messages.Duration_Day : string.Format(Messages.Duration_Days, days);
}
