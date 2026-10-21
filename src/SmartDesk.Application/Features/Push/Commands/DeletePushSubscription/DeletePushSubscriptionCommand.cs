using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.Push.Commands.DeletePushSubscription;

// Turns notifications off for one of the caller's devices (DELETE /api/push/subscription,
// ADR-0056), as the app does when they're switched off there and when its user signs out. A device
// that isn't theirs, or isn't there, changes nothing.
public sealed record DeletePushSubscriptionCommand(string Endpoint) : IRequest;

public sealed class DeletePushSubscriptionCommandValidator : AbstractValidator<DeletePushSubscriptionCommand>
{
    public DeletePushSubscriptionCommandValidator() =>
        RuleFor(x => x.Endpoint).NotEmpty().MaximumLength(PushRules.MaxEndpointLength);
}

public sealed class DeletePushSubscriptionCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<DeletePushSubscriptionCommand>
{
    public async Task Handle(DeletePushSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        await db.PushSubscriptions
            .Where(s => s.UserId == userId && s.Endpoint == request.Endpoint)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
