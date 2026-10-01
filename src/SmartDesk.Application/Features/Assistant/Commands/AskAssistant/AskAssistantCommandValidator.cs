using FluentValidation;
using SmartDesk.Application.Resources;

namespace SmartDesk.Application.Features.Assistant.Commands.AskAssistant;

public sealed class AskAssistantCommandValidator : AbstractValidator<AskAssistantCommand>
{
    // A long conversation costs more with every message; the app starts a new one before this.
    public const int MaxMessages = 40;

    public const int MaxMessageLength = 2000;

    public AskAssistantCommandValidator()
    {
        RuleFor(x => x.Messages)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(_ => Messages.Assistant_NoMessage)
            .Must(messages => messages.Count <= MaxMessages)
            .WithMessage(_ => string.Format(Messages.Assistant_TooManyMessages, MaxMessages))
            .Must(messages => messages[^1]?.Role == AssistantMessageDto.User)
            .WithMessage(_ => Messages.Assistant_LastFromUser);

        RuleForEach(x => x.Messages)
            .NotNull()
            .WithMessage(_ => Messages.Assistant_NoMessage)
            .ChildRules(message =>
            {
                message.RuleFor(m => m.Role)
                    .Must(role => role is AssistantMessageDto.User or AssistantMessageDto.Assistant)
                    .WithMessage(_ => Messages.Assistant_UnknownRole);
                message.RuleFor(m => m.Text)
                    .Cascade(CascadeMode.Stop)
                    .Must(text => !string.IsNullOrWhiteSpace(text))
                    .WithMessage(_ => Messages.Assistant_NoMessage)
                    .MaximumLength(MaxMessageLength)
                    .WithMessage(_ => string.Format(Messages.Assistant_MessageTooLong, MaxMessageLength));
            });

        RuleFor(x => x.TimeZone)
            .Must(id => TimeZoneInfo.TryFindSystemTimeZoneById(id!, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.TimeZone))
            .WithMessage(_ => Messages.Validation_UnknownTimeZone);
    }
}
