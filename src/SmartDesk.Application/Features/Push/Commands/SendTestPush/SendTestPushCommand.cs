using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Push.Common;
using SmartDesk.Application.Resources;

namespace SmartDesk.Application.Features.Push.Commands.SendTestPush;

// A notification to every device the caller has them on (POST /api/push/test, ADR-0056), to see one
// arrive. Says how many devices it went to; with none, nothing is sent.
public sealed record SendTestPushCommand : IRequest<PushTestDto>;

public sealed record PushTestDto(int Devices);

public sealed class SendTestPushCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, IPushSender push)
    : IRequestHandler<SendTestPushCommand, PushTestDto>
{
    public async Task<PushTestDto> Handle(SendTestPushCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        if (push.PublicKey is null)
        {
            throw new ServiceUnavailableException(Messages.Push_Off);
        }

        var devices = await db.PushSubscriptions.CountAsync(s => s.UserId == userId, cancellationToken);
        if (devices > 0)
        {
            push.Queue(new PushNotice(PushTopic.Test, [userId]));
        }

        return new PushTestDto(devices);
    }
}
