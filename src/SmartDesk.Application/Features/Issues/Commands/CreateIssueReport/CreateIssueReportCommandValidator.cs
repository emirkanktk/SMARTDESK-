using FluentValidation;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.Issues.Commands.CreateIssueReport;

// The photo follows a resource photo's rules (ADR-0027), reported on "Photo", the form field
// it comes in.
public sealed class CreateIssueReportCommandValidator : AbstractValidator<CreateIssueReportCommand>
{
    public CreateIssueReportCommandValidator()
    {
        RuleFor(x => x.ResourceId).NotEmpty();
        RuleFor(x => x.Category).NotNull().IsInEnum();
        RuleFor(x => x.Description)
            .Must(description => !string.IsNullOrWhiteSpace(description))
            .WithMessage(_ => Messages.Issues_DescriptionMissing)
            .MaximumLength(IssueRules.MaxDescriptionLength);

        When(x => x.Photo is not null, () =>
            RuleFor(x => x.Photo!)
                .Cascade(CascadeMode.Stop)
                .Must(data => data.Length <= ResourcePhotoRules.MaxBytes)
                .WithMessage(_ => string.Format(Messages.Resources_PhotoTooLarge, ResourcePhotoRules.MaxBytes / (1024 * 1024)))
                .Must(data => ResourcePhotoRules.DetectContentType(data) is not null)
                .WithMessage(_ => Messages.Resources_PhotoType)
                .OverridePropertyName("Photo"));
    }
}
