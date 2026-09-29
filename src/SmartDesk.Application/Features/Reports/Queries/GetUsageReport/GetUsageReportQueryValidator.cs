using FluentValidation;
using SmartDesk.Application.Features.Reports.Common;
using SmartDesk.Application.Resources;

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
            .WithMessage(_ => Messages.Reports_ToOnOrAfterFrom)
            .Must((query, to) => to.DayNumber - query.From.DayNumber + 1 <= ReportRules.MaxRangeDays)
            .WithMessage(_ => string.Format(Messages.Reports_MaxRange, ReportRules.MaxRangeDays));

        RuleFor(x => x.TimeZone)
            .Must(id => TimeZoneInfo.TryFindSystemTimeZoneById(id!, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.TimeZone))
            .WithMessage(_ => Messages.Validation_UnknownTimeZone);
    }
}
