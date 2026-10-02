using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Assistant.Commands.AskAssistant;
using SmartDesk.Application.Features.Assistant.Common;
using SmartDesk.Application.Features.Assistant.Queries.GetAssistantStatus;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Application.Features.Resources.Queries.GetAvailableResources;
using SmartDesk.Application.Features.Resources.Queries.GetResourceById;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Enums;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Assistant;

public class AskAssistantCommandHandlerTests
{
    // Monday 5 October 2026, 09:00 in Istanbul.
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 6, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly ScriptedModel _model = new();
    private readonly Mock<ISender> _sender = new();
    private readonly Mock<IIdentityService> _identity = new();

    public AskAssistantCommandHandlerTests() =>
        _identity.Setup(i => i.FindByIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserProfile(UserId, "Ayşe", "Yılmaz", "ayse@smartdesk.local", [Roles.Employee]));

    private Task<AssistantReplyDto> AskAsync(string? timeZone, params AssistantMessageDto[] messages) =>
        new AskAssistantCommandHandler(_model, _sender.Object, new FakeCurrentUser(UserId, Roles.Employee), _identity.Object, new FakeTimeProvider(Now))
            .Handle(new AskAssistantCommand(messages, timeZone), CancellationToken.None);

    [Fact]
    public async Task Handle_NoModelSetUp_IsUnavailable()
    {
        var act = () => new AskAssistantCommandHandler(new ScriptedModel(configured: false), _sender.Object,
                new FakeCurrentUser(UserId, Roles.Employee), _identity.Object, new FakeTimeProvider(Now))
            .Handle(new AskAssistantCommand([new(AssistantMessageDto.User, "Hi")], null), CancellationToken.None);

        await act.Should().ThrowAsync<ServiceUnavailableException>().WithMessage(Messages.Assistant_NotConfigured);
    }

    [Fact]
    public async Task Handle_TellsTheModelTheConversationAndWhoAndWhenItIs()
    {
        using var _ = UiCulture.Use("tr-TR");

        await AskAsync("Europe/Istanbul",
            new(AssistantMessageDto.Assistant, "Merhaba! Ne arıyorsunuz?"),
            new(AssistantMessageDto.User, "  Yarın 10'da 4 kişilik oda var mı? "));

        _model.Request!.Instructions.Should().Be(AssistantInstructions.Text);
        _model.Request.Context.Should().Be(
            "Now: Monday 2026-10-05 09:00 in Europe/Istanbul (UTC+03:00). Person: Ayşe Yılmaz (Employee). Language: Turkish.");
        _model.Request.Conversation.Should().Equal(
            new AssistantMessage(AssistantRole.Assistant, "Merhaba! Ne arıyorsunuz?"),
            new AssistantMessage(AssistantRole.User, "Yarın 10'da 4 kişilik oda var mı?"));
    }

    [Fact]
    public async Task Handle_NoTimeZone_WorksInUtc()
    {
        await AskAsync(null, new AssistantMessageDto(AssistantMessageDto.User, "Is Room A free?"));

        _model.Request!.Context.Should().StartWith("Now: Monday 2026-10-05 06:00 in UTC (UTC+00:00).").And.EndWith("Language: English.");
    }

    [Fact]
    public async Task Handle_ReturnsTheReplyWithTheCardsTheModelProposed()
    {
        var room = new ResourceDto(Guid.NewGuid(), "Room A", Guid.NewGuid(), "Meeting Room", 6, "2nd Floor",
            ResourceStatus.Active, null, [], [], null, false);
        _sender.Setup(s => s.Send(new GetResourceByIdQuery(room.Id), It.IsAny<CancellationToken>())).ReturnsAsync(room);
        _sender.Setup(s => s.Send(It.IsAny<GetAvailableResourcesQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync([room]);
        _model.Answer = async toolbox =>
        {
            await toolbox.RunAsync(AssistantToolbox.ProposeBooking, JsonSerializer.SerializeToElement(
                new { resourceId = room.Id, start = "2026-10-06T10:00", end = "2026-10-06T11:00" }), CancellationToken.None);
            return "Room A is free then; the card is ready.";
        };

        var reply = await AskAsync("Europe/Istanbul", new AssistantMessageDto(AssistantMessageDto.User, "Book Room A tomorrow at 10."));

        reply.Reply.Should().Be("Room A is free then; the card is ready.");
        reply.Proposals.Should().ContainSingle().Which.Should().Be(new AssistantProposalDto(
            AssistantProposalDto.Book, room.Id, null, "Room A",
            new DateTime(2026, 10, 6, 7, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 6, 8, 0, 0, DateTimeKind.Utc), null));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Status_SaysWhetherTheServerHasAnAssistant(bool configured)
    {
        var status = await new GetAssistantStatusQueryHandler(new ScriptedModel(configured))
            .Handle(new GetAssistantStatusQuery(), CancellationToken.None);

        status.Enabled.Should().Be(configured);
    }

    // Stands in for the language model: keeps what it was asked and answers with a script, which
    // can use the tools as the model would.
    private sealed class ScriptedModel(bool configured = true) : IAssistantModel
    {
        public AssistantRequest? Request { get; private set; }

        public Func<IAssistantToolbox, Task<string>> Answer { get; set; } = _ => Task.FromResult("Hello!");

        public bool IsConfigured => configured;

        public Task<string> AnswerAsync(AssistantRequest request, IAssistantToolbox toolbox, CancellationToken cancellationToken)
        {
            Request = request;
            return Answer(toolbox);
        }
    }
}
