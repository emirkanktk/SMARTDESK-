using FluentValidation;

namespace SmartDesk.Application.Features.Dashboard.Queries.GetDashboard;

public sealed class GetDashboardQueryValidator : AbstractValidator<GetDashboardQuery>
{
    public GetDashboardQueryValidator()
    {
        RuleFor(x => x.TimeZone)
            .Must(id => TimeZoneInfo.TryFindSystemTimeZoneById(id!, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.TimeZone))
            .WithMessage("Unknown time zone. Use an IANA id such as 'Europe/Istanbul'.");
    }
}
