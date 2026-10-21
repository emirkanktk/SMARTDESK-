using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.Push.Commands.SavePushSubscription;

// Turns notifications on for the caller's device (PUT /api/push/subscription, ADR-0056): the
// subscription the browser made, kept by its address. The same device again - the app sends it on
// every start - only refreshes it; a device someone else had them on is the caller's now. Past the
// most devices per person, the one seen longest ago is forgotten.
public sealed record SavePushSubscriptionCommand(string Endpoint, string P256dh, string Auth) : IRequest;

public sealed class SavePushSubscriptionCommandHandler(
    IApplicationDbContext db, ICurrentUserService currentUser, IPushSender push, TimeProvider timeProvider)
    : IRequestHandler<SavePushSubscriptionCommand>
{
    public async Task Handle(SavePushSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        if (push.PublicKey is null)
        {
            throw new ServiceUnavailableException(Messages.Push_Off);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var device = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == request.Endpoint, cancellationToken);
        if (device is null)
        {
            db.PushSubscriptions.Add(new PushSubscription
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Endpoint = request.Endpoint,
                P256dh = request.P256dh,
                Auth = request.Auth,
                SavedAt = now,
            });
        }
        else
        {
            device.UserId = userId;
            device.P256dh = request.P256dh;
            device.Auth = request.Auth;
            device.SavedAt = now;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (device is null)
        {
            // Saved by another tab of the same device meanwhile: it's on either way.
            db.DiscardChanges();
            if (!await db.PushSubscriptions.AnyAsync(s => s.Endpoint == request.Endpoint, cancellationToken))
            {
                throw;
            }
        }

        var forgotten = await db.PushSubscriptions
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.SavedAt)
            .Skip(PushRules.MaxDevicesPerUser)
            .ToListAsync(cancellationToken);
        if (forgotten.Count > 0)
        {
            db.PushSubscriptions.RemoveRange(forgotten);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
