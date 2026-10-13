using SmartDesk.Application.Features.Reports.Queries.GetUsageReport;

namespace SmartDesk.Application.Features.Reports.Common;

// The usage report as an Excel workbook (ADR-0045), its words in the request's language.
public interface IUsageReportWorkbook
{
    byte[] Write(UsageReportDto report);
}
