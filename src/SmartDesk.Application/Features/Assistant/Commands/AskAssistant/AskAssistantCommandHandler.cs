using System.Globalization;
using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Assistant.Common;
using SmartDesk.Application.Resources;

namespace SmartDesk.Application.Features.Assistant.Commands.AskAssistant;

public sealed class AskAssistantCommandHandler(
    IAssistantModel model,
    ISender sender,
    ICurrentUserService currentUser,
    IIdentityService identityService,
    TimeProvider timeProvider) : IRequestHandler<AskAssistantCommand, AssistantReplyDto>
{
    public async Task<AssistantReplyDto> Handle(AskAssistantCommand request, CancellationToken cancellationToken)
    {
        if (!model.IsConfigured)
        {
            throw new ServiceUnavailableException(Messages.Assistant_NotConfigured);
        }

        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var zone = string.IsNullOrWhiteSpace(request.TimeZone)
            ? TimeZoneInfo.Utc
            : TimeZoneInfo.FindSystemTimeZoneById(request.TimeZone);
        var user = await identityService.FindByIdAsync(userId, cancellationToken);

        var toolbox = new AssistantToolbox(sender, zone, timeProvider);
        var conversation = request.Messages
            .Select(m => new AssistantMessage(
                m.Role == AssistantMessageDto.Assistant ? AssistantRole.Assistant : AssistantRole.User,
                m.Text.Trim()))
            .ToList();
        var context = AssistantInstructions.Context(timeProvider.GetUtcNow().UtcDateTime, zone, user, CultureInfo.CurrentUICulture);

        var reply = await model.AnswerAsync(
            new AssistantRequest(AssistantInstructions.Text, context, conversation), toolbox, cancellationToken);
        return new AssistantReplyDto(reply, toolbox.Proposals);
    }
}
