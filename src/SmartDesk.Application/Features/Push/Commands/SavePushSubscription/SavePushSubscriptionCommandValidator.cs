using FluentValidation;
using SmartDesk.Application.Features.Push.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.Push.Commands.SavePushSubscription;

// Only a known push service's address (PushRules.KnownServices): the server will post to it, and an
// address on this machine or the office network must never get a request on someone's say-so. The
// keys must be ones a notification can be encrypted with (RFC 8291).
public sealed class SavePushSubscriptionCommandValidator : AbstractValidator<SavePushSubscriptionCommand>
{
    public SavePushSubscriptionCommandValidator()
    {
        RuleFor(x => x.Endpoint)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(PushRules.MaxEndpointLength)
            .Must(endpoint => Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && PushRules.IsKnownService(uri))
            .WithMessage(_ => Messages.Push_UnknownService);
        RuleFor(x => x.P256dh).Must(PushKeys.IsPublicKey).WithMessage(_ => Messages.Push_BadKey);
        RuleFor(x => x.Auth).Must(PushKeys.IsAuthSecret).WithMessage(_ => Messages.Push_BadKey);
    }
}
