using MediatR;
using SmartDesk.Application.Common.Interfaces;

namespace SmartDesk.Application.Features.Assistant.Queries.GetAssistantStatus;

// Whether this server has an assistant (ADR-0029): the app shows it only then.
public sealed record GetAssistantStatusQuery : IRequest<AssistantStatusDto>;

public sealed record AssistantStatusDto(bool Enabled);

public sealed class GetAssistantStatusQueryHandler(IAssistantModel model)
    : IRequestHandler<GetAssistantStatusQuery, AssistantStatusDto>
{
    public Task<AssistantStatusDto> Handle(GetAssistantStatusQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(new AssistantStatusDto(model.IsConfigured));
}
