using ClosedXML.Excel;
using FluentAssertions;
using SmartDesk.Application.Features.Reports.Common;
using SmartDesk.Application.Features.Reports.Queries.GetUsageReport;
using SmartDesk.Domain.Enums;
using SmartDesk.Infrastructure.Reports;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Reports;

// The usage report as an Excel workbook (ADR-0045), read back as Excel would.
public class UsageReportWorkbookTests
{
    private static readonly Guid RoomType = Guid.NewGuid();

    private static UsageReportDto Report(IReadOnlyList<ResourceUsage>? resources = null) =>
        new(
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30),
            "Europe/Istanbul",
            new DateTime(2026, 8, 31, 21, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 30, 21, 0, 0, DateTimeKind.Utc),
            "Mon-Fri 09:00-18:00",
            new TimeOnly(9, 0),
            new TimeOnly(18, 0),
            198,
            new UsageTotals(2, 14, 3, 120.5, 98, 396, 0.2475, 1, 3, 0.25, 0.1176),
            resources ??
            [
                new ResourceUsage(Guid.NewGuid(), "Board Room", RoomType, "Meeting Room", ResourceStatus.Active,
                    10, 2, 70.25, 66, 0.3333, 1, 3, 0.25),
                new ResourceUsage(Guid.NewGuid(), "Camera C-1", Guid.NewGuid(), "Camera", ResourceStatus.Maintenance,
                    4, 1, 50.25, 32, 0.1616, 0, 0, 0),
            ],
            [new ResourceTypeUsage(RoomType, "Meeting Room", 1, 10, 2, 70.25, 66, 198, 0.3333, 1, 3, 0.25)],
            [new UserUsage(Guid.NewGuid(), "Ayşe Yılmaz", 9, 60.5), new UserUsage(Guid.NewGuid(), null, 1, 2)],
            Enumerable.Range(0, 7)
                .Select(d => new WeekdayHours(
                    (DayOfWeek)((d + 1) % 7),
                    Enumerable.Range(0, 24).Select(h => d == 0 && h == 10 ? 0.6 : 0.0).ToList()))
                .ToList());

    private static XLWorkbook Open(UsageReportDto report) =>
        new(new MemoryStream(new UsageReportWorkbook().Write(report)));

    [Fact]
    public void ItHasASheetForEachPart_WithTheSummaryFirst()
    {
        using var workbook = Open(Report());

        workbook.Worksheets.Select(s => s.Name).Should().Equal("Summary", "Resources", "Types", "People", "By hour");
        var summary = workbook.Worksheet("Summary");
        summary.Cell(1, 1).GetString().Should().Be("SmartDesk usage report");
        summary.Cell(3, 2).GetDateTime().Should().Be(new DateTime(2026, 9, 1));
        summary.Cell(5, 2).GetString().Should().Be("Europe/Istanbul");
        Value(summary, "Reservations").Should().Be(14);
        Value(summary, "No-shows").Should().Be(1);
        Value(summary, "Utilization").Should().Be(0.2475);
        summary.Cell(Row(summary, "Utilization"), 2).Style.NumberFormat.Format.Should().Be("0.0%");
    }

    [Fact]
    public void TheResourcesAreATable_OfNumbersExcelCanSortAndSum()
    {
        using var workbook = Open(Report());

        var sheet = workbook.Worksheet("Resources");
        var table = sheet.Tables.Should().ContainSingle().Subject;
        table.Fields.Select(f => f.Name).Should().Equal(
            "Resource", "Type", "Status", "Utilization", "Booked hours", "Office hours booked", "Reservations",
            "Cancelled", "No-shows", "Checked in", "No-show rate");
        table.DataRange.RowCount().Should().Be(2);
        sheet.Cell(2, 1).GetString().Should().Be("Board Room");
        sheet.Cell(2, 4).GetDouble().Should().Be(0.3333);
        sheet.Cell(2, 5).GetDouble().Should().Be(70.25);
        sheet.Cell(3, 3).GetString().Should().Be("Maintenance");
        workbook.Worksheet("People").Cell(3, 1).GetString().Should().Be("Unknown user");
    }

    [Fact]
    public void TheHeatMap_IsTheHoursOfEachWeekday_ColouredByHowFullTheyWere()
    {
        using var workbook = Open(Report());

        var sheet = workbook.Worksheet("By hour");
        sheet.Cell(3, 2).GetString().Should().Be("Monday");
        sheet.Cell(3, 8).GetString().Should().Be("Sunday");
        sheet.Cell(4, 1).GetString().Should().Be("00:00");
        sheet.Cell(4 + 10, 2).GetDouble().Should().Be(0.6); // Monday 10:00
        sheet.ConditionalFormats.Should().ContainSingle().Which.ConditionalFormatType.Should().Be(XLConditionalFormatType.ColorScale);
    }

    [Fact]
    public void ItIsWrittenInTheRequestsLanguage()
    {
        using var _ = UiCulture.Use("tr-TR");
        using var workbook = Open(Report());

        workbook.Worksheets.Select(s => s.Name).Should().Equal("Özet", "Kaynaklar", "Türler", "Kişiler", "Saatlere göre");
        var resources = workbook.Worksheet("Kaynaklar");
        resources.Cell(1, 1).GetString().Should().Be("Kaynak");
        resources.Cell(2, 2).GetString().Should().Be("Toplantı odası");
        resources.Cell(3, 3).GetString().Should().Be("Bakımda");
        workbook.Worksheet("Saatlere göre").Cell(3, 2).GetString().Should().Be("Pazartesi");
    }

    [Fact]
    public void ATableWithNothingInIt_IsJustItsHeader()
    {
        using var workbook = Open(Report(resources: []));

        var sheet = workbook.Worksheet("Resources");
        sheet.Tables.Should().BeEmpty();
        sheet.Cell(1, 1).GetString().Should().Be("Resource");
        sheet.Cell(1, 1).Style.Font.Bold.Should().BeTrue();
    }

    private static int Row(IXLWorksheet sheet, string label) =>
        sheet.Column(1).CellsUsed().Single(c => c.GetString() == label).Address.RowNumber;

    private static double Value(IXLWorksheet sheet, string label) => sheet.Cell(Row(sheet, label), 2).GetDouble();
}
