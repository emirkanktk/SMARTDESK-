using System.Globalization;
using ClosedXML.Excel;
using SmartDesk.Application.Common.Localization;
using SmartDesk.Application.Features.Reports.Common;
using SmartDesk.Application.Features.Reports.Queries.GetUsageReport;
using SmartDesk.Application.Resources;

namespace SmartDesk.Infrastructure.Reports;

// The usage report as an Excel workbook (ADR-0045): a summary, then resources, types, people and
// the heat map, a sheet each. Words in the request's language; numbers as numbers, formatted by
// Excel, so they sort, sum and chart there. Column widths are set rather than measured: measuring
// needs fonts a server or container may not have.
public sealed class UsageReportWorkbook : IUsageReportWorkbook
{
    private const string Percent = "0.0%";
    private const string Hours = "0.0#";
    private const string Date = "yyyy-mm-dd";

    // The app's primary colour, for the heat map's fullest hours.
    private static readonly XLColor Full = XLColor.FromHtml("#4F46E5");

    public byte[] Write(UsageReportDto report)
    {
        using var workbook = new XLWorkbook();
        workbook.Properties.Title = Messages.Report_Title;
        workbook.Properties.Author = "SmartDesk";

        Summary(workbook.AddWorksheet(Messages.Report_Summary), report);
        Resources(workbook.AddWorksheet(Messages.Report_Resources), report.Resources);
        Types(workbook.AddWorksheet(Messages.Report_Types), report.ByType);
        People(workbook.AddWorksheet(Messages.Report_People), report.TopUsers);
        ByHour(workbook.AddWorksheet(Messages.Report_ByHour), report.HeatMap);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void Summary(IXLWorksheet sheet, UsageReportDto report)
    {
        var title = sheet.Cell(1, 1);
        title.Value = Messages.Report_Title;
        title.Style.Font.Bold = true;
        title.Style.Font.FontSize = 14;

        var totals = report.Totals;
        (string Label, XLCellValue Value, string? Format)[] lines =
        [
            (Messages.Report_From, report.From.ToDateTime(TimeOnly.MinValue), Date),
            (Messages.Report_To, report.To.ToDateTime(TimeOnly.MinValue), Date),
            (Messages.Report_TimeZone, report.TimeZone, null),
            (Messages.Report_OfficeHours, Messages.Report_OfficeHoursValue, null),
            (string.Empty, Blank.Value, null),
            (Messages.Report_ResourceCount, totals.Resources, null),
            (Messages.Report_Reservations, totals.Reservations, null),
            (Messages.Report_Cancelled, totals.Cancelled, null),
            (Messages.Report_NoShows, totals.NoShows, null),
            (Messages.Report_CheckedIn, totals.CheckedIn, null),
            (Messages.Report_BookedHours, totals.BookedHours, Hours),
            (Messages.Report_OfficeHoursBooked, totals.WorkingHoursBooked, Hours),
            (Messages.Report_OfficeHoursAvailable, totals.WorkingHoursAvailable, Hours),
            (Messages.Report_Utilization, totals.Utilization, Percent),
            (Messages.Report_NoShowRate, totals.NoShowRate, Percent),
            (Messages.Report_CancellationRate, totals.CancellationRate, Percent),
        ];
        for (var i = 0; i < lines.Length; i++)
        {
            var (label, value, format) = lines[i];
            sheet.Cell(3 + i, 1).Value = label;
            sheet.Cell(3 + i, 1).Style.Font.Bold = true;
            var cell = sheet.Cell(3 + i, 2);
            cell.Value = value;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            if (format is not null)
            {
                cell.Style.NumberFormat.Format = format;
            }
        }

        sheet.Column(1).Width = 34;
        sheet.Column(2).Width = 22;
    }

    private static void Resources(IXLWorksheet sheet, IReadOnlyList<ResourceUsage> rows)
    {
        Table(
            sheet,
            "Resources",
            [
                (Messages.Report_Resource, 30, null),
                (Messages.Report_Type, 18, null),
                (Messages.Report_Status, 14, null),
                (Messages.Report_Utilization, 12, Percent),
                (Messages.Report_BookedHours, 16, Hours),
                (Messages.Report_OfficeHoursBooked, 20, Hours),
                (Messages.Report_Reservations, 14, null),
                (Messages.Report_Cancelled, 12, null),
                (Messages.Report_NoShows, 12, null),
                (Messages.Report_CheckedIn, 14, null),
                (Messages.Report_NoShowRate, 14, Percent),
            ],
            rows.Select(r => new XLCellValue[]
            {
                r.ResourceName, Languages.DescribeType(r.ResourceTypeName), Languages.Describe(r.Status), r.Utilization,
                r.BookedHours, r.WorkingHoursBooked, r.Reservations, r.Cancelled, r.NoShows, r.CheckedIn, r.NoShowRate,
            }));
    }

    private static void Types(IXLWorksheet sheet, IReadOnlyList<ResourceTypeUsage> rows)
    {
        Table(
            sheet,
            "Types",
            [
                (Messages.Report_Type, 22, null),
                (Messages.Report_ResourceCount, 14, null),
                (Messages.Report_Utilization, 12, Percent),
                (Messages.Report_BookedHours, 16, Hours),
                (Messages.Report_OfficeHoursBooked, 20, Hours),
                (Messages.Report_OfficeHoursAvailable, 22, Hours),
                (Messages.Report_Reservations, 14, null),
                (Messages.Report_Cancelled, 12, null),
                (Messages.Report_NoShows, 12, null),
                (Messages.Report_NoShowRate, 14, Percent),
            ],
            rows.Select(t => new XLCellValue[]
            {
                Languages.DescribeType(t.ResourceTypeName), t.Resources, t.Utilization, t.BookedHours, t.WorkingHoursBooked,
                t.WorkingHoursAvailable, t.Reservations, t.Cancelled, t.NoShows, t.NoShowRate,
            }));
    }

    private static void People(IXLWorksheet sheet, IReadOnlyList<UserUsage> rows)
    {
        Table(
            sheet,
            "People",
            [
                (Messages.Report_Person, 30, null),
                (Messages.Report_Reservations, 14, null),
                (Messages.Report_BookedHours, 16, Hours),
            ],
            rows.Select(u => new XLCellValue[] { u.DisplayName ?? Messages.Report_UnknownUser, u.Reservations, u.BookedHours }));
    }

    // A row per hour, a column per weekday, coloured from white at 0% to the primary colour at
    // 100%, as on the page.
    private static void ByHour(IXLWorksheet sheet, IReadOnlyList<WeekdayHours> days)
    {
        var note = sheet.Cell(1, 1);
        note.Value = Messages.Report_ByHourNote;
        note.Style.Font.Italic = true;

        const int header = 3;
        sheet.Cell(header, 1).Value = Messages.Report_Hour;
        for (var d = 0; d < days.Count; d++)
        {
            sheet.Cell(header, 2 + d).Value = CultureInfo.CurrentUICulture.DateTimeFormat.GetDayName(days[d].Day);
            sheet.Column(2 + d).Width = 13;
        }

        sheet.Row(header).Style.Font.Bold = true;
        for (var hour = 0; hour < 24; hour++)
        {
            sheet.Cell(header + 1 + hour, 1).Value = $"{hour:00}:00";
            for (var d = 0; d < days.Count; d++)
            {
                var cell = sheet.Cell(header + 1 + hour, 2 + d);
                cell.Value = days[d].Hours[hour];
                cell.Style.NumberFormat.Format = Percent;
            }
        }

        if (days.Count > 0)
        {
            sheet.Range(header + 1, 2, header + 24, 1 + days.Count)
                .AddConditionalFormat()
                .ColorScale()
                .Minimum(XLCFContentType.Number, 0, XLColor.White)
                .Maximum(XLCFContentType.Number, 1, Full);
        }

        sheet.Column(1).Width = 10;
        sheet.SheetView.FreezeRows(header);
    }

    // A header row and the rows under it, as an Excel table: sortable and filterable there.
    private static void Table(
        IXLWorksheet sheet,
        string name,
        (string Title, double Width, string? Format)[] columns,
        IEnumerable<XLCellValue[]> rows)
    {
        for (var c = 0; c < columns.Length; c++)
        {
            sheet.Cell(1, 1 + c).Value = columns[c].Title;
            sheet.Column(1 + c).Width = columns[c].Width;
        }

        var count = 0;
        foreach (var row in rows)
        {
            count++;
            for (var c = 0; c < columns.Length; c++)
            {
                var cell = sheet.Cell(1 + count, 1 + c);
                cell.Value = row[c];
                if (columns[c].Format is { } format)
                {
                    cell.Style.NumberFormat.Format = format;
                }
            }
        }

        // Excel wants a row under a table's header; with none, the header is bold instead.
        if (count > 0)
        {
            sheet.Range(1, 1, 1 + count, columns.Length).CreateTable(name).Theme = XLTableTheme.TableStyleMedium2;
        }
        else
        {
            sheet.Row(1).Style.Font.Bold = true;
        }

        sheet.SheetView.FreezeRows(1);
    }
}
