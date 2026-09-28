using FluentValidation;
using SmartDesk.Application.Features.Reports.Common;

namespace SmartDesk.Application.Features.Reports.Queries.GetUsageReport;

public sealed class GetUsageReportQueryValidator : AbstractValidator<GetUsageReportQuery>
{
    public GetUsageReportQueryValidator()
    {
        RuleFor(x => x.From).NotEmpty();

        RuleFor(x => x.To)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must((query, to) => to >= query.From)
            .WithMessage("'To' must be on or after 'From'.")
            .Must((query, to) => to.DayNumber - query.From.DayNumber + 1 <= ReportRules.MaxRangeDays)
            .WithMessage($"The report can cover at most {ReportRules.MaxRangeDays} days.");

        RuleFor(x => x.TimeZone)
            .Must(id => TimeZoneInfo.TryFindSystemTimeZoneById(id!, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.TimeZone))
            .WithMessage("Unknown time zone. Use an IANA id such as 'Europe/Istanbul'.");
    }
}
