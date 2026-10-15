using System.Text;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using SmartDesk.Application.Features.Calendar.Common;
using IcalCalendar = Ical.Net.Calendar;

namespace SmartDesk.Infrastructure.Calendar;

// A calendar as iCalendar text (RFC 5545), written with Ical.Net (ADR-0048), which folds long
// lines and escapes commas, semicolons and line breaks the way calendar apps read them. Times go
// out in UTC. A feed also says how often to fetch it again, in the standard REFRESH-INTERVAL
// (RFC 7986) and in X-PUBLISHED-TTL, which Outlook reads.
public sealed class IcalCalendarWriter : ICalendarWriter
{
    public const string ProductId = "-//SmartDesk//SmartDesk//EN";

    public byte[] Write(CalendarDocument document)
    {
        var calendar = new IcalCalendar { ProductId = ProductId, Method = CalendarMethods.Publish };
        calendar.AddProperty("X-WR-CALNAME", document.Name);
        if (document.RefreshInterval is { } refresh)
        {
            var interval = new CalendarProperty("REFRESH-INTERVAL", Duration(refresh));
            interval.AddParameter("VALUE", "DURATION");
            calendar.AddProperty(interval);
            calendar.AddProperty("X-PUBLISHED-TTL", Duration(refresh));
        }

        var stamp = Utc(document.WrittenAt);
        foreach (var entry in document.Entries)
        {
            calendar.Events.Add(new CalendarEvent
            {
                Uid = entry.Uid,
                DtStamp = stamp,
                DtStart = Utc(entry.Start),
                DtEnd = Utc(entry.End),
                Summary = entry.Summary,
                Location = string.IsNullOrWhiteSpace(entry.Location) ? null : entry.Location,
                Description = entry.Description,
                Url = new Uri(entry.Url),
                Status = entry.Status switch
                {
                    CalendarEntryStatus.Tentative => EventStatus.Tentative,
                    CalendarEntryStatus.Cancelled => EventStatus.Cancelled,
                    _ => EventStatus.Confirmed,
                },
            });
        }

        return Encoding.UTF8.GetBytes(new CalendarSerializer().SerializeToString(calendar)!);
    }

    private static CalDateTime Utc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc), CalDateTime.UtcTzId);

    // "PT1H", "PT30M": whole hours and minutes, as a refresh interval needs no more.
    private static string Duration(TimeSpan span) =>
        span.Minutes == 0 ? $"PT{(int)span.TotalHours}H" : $"PT{(int)span.TotalMinutes}M";
}
