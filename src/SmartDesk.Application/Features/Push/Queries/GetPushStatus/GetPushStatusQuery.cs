using MediatR;
using SmartDesk.Application.Common.Interfaces;

namespace SmartDesk.Application.Features.Push.Queries.GetPushStatus;

// Whether this server sends notifications, and the key a browser subscribes with (GET /api/push,
// ADR-0056). Without the server's VAPID keys it's off, and the app offers nothing.
public sealed record GetPushStatusQuery : IRequest<PushStatusDto>;

public sealed record PushStatusDto(bool Enabled, string? PublicKey);

public sealed class GetPushStatusQueryHandler(IPushSender push) : IRequestHandler<GetPushStatusQuery, PushStatusDto>
{
    public Task<PushStatusDto> Handle(GetPushStatusQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(new PushStatusDto(push.PublicKey is not null, push.PublicKey));
}
