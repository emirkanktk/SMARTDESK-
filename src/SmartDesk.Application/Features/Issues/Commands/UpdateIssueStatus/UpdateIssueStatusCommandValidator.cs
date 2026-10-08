using FluentValidation;

namespace SmartDesk.Application.Features.Issues.Commands.UpdateIssueStatus;

public sealed class UpdateIssueStatusCommandValidator : AbstractValidator<UpdateIssueStatusCommand>
{
    public UpdateIssueStatusCommandValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
    }
}
