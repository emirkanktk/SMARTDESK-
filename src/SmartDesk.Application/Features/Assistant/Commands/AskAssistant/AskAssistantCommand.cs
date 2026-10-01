using MediatR;
using SmartDesk.Application.Features.Assistant.Common;

namespace SmartDesk.Application.Features.Assistant.Commands.AskAssistant;

// The conversation so far, ending with the user's new message (ADR-0029). The server keeps
// nothing between requests: the app sends the whole conversation each time. TimeZone is the
// browser's IANA zone, for "tomorrow at 10"; UTC when left out.
public sealed record AskAssistantCommand(IReadOnlyList<AssistantMessageDto> Messages, string? TimeZone)
    : IRequest<AssistantReplyDto>;

// Role: "user" or "assistant".
public sealed record AssistantMessageDto(string Role, string Text)
{
    public const string User = "user";
    public const string Assistant = "assistant";
}

public sealed record AssistantReplyDto(string Reply, IReadOnlyList<AssistantProposalDto> Proposals);
