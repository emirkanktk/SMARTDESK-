using FluentValidation;
using SmartDesk.Application.Features.Meetings.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Meetings.Commands.RespondToInvitation;

public sealed class RespondToInvitationCommandValidator : AbstractValidator<RespondToInvitationCommand>
{
    public RespondToInvitationCommandValidator()
    {
        // Pending is where everyone starts, not an answer.
        RuleFor(x => x.Response)
            .Must(response => response is AttendeeResponse.Accepted or AttendeeResponse.Declined)
            .WithMessage(_ => Messages.Meetings_AnswerInvalid);

        // Not coming says why: the organizer may move the meeting for it.
        RuleFor(x => x.Reason)
            .Must(reason => !string.IsNullOrWhiteSpace(reason))
            .When(x => x.Response == AttendeeResponse.Declined)
            .WithMessage(_ => Messages.Meetings_ReasonRequired);

        RuleFor(x => x.Reason).MaximumLength(MeetingRules.ReasonMaxLength);
    }
}
