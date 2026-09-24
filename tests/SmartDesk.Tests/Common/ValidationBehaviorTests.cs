using FluentAssertions;
using FluentValidation;
using MediatR;
using SmartDesk.Application.Common.Behaviors;

namespace SmartDesk.Tests.Common;

public class ValidationBehaviorTests
{
    public sealed record SampleRequest(string Name) : IRequest<string>;

    private sealed class NameRequiredValidator : AbstractValidator<SampleRequest>
    {
        public NameRequiredValidator() => RuleFor(x => x.Name).NotEmpty();
    }

    private bool _nextCalled;

    private RequestHandlerDelegate<string> Next => _ =>
    {
        _nextCalled = true;
        return Task.FromResult("handled");
    };

    [Fact]
    public async Task Handle_NoValidatorsRegistered_CallsHandler()
    {
        var behavior = new ValidationBehavior<SampleRequest, string>([]);

        var result = await behavior.Handle(new SampleRequest(""), Next, CancellationToken.None);

        result.Should().Be("handled");
    }

    [Fact]
    public async Task Handle_ValidRequest_CallsHandler()
    {
        var behavior = new ValidationBehavior<SampleRequest, string>([new NameRequiredValidator()]);

        var result = await behavior.Handle(new SampleRequest("Meeting Room A"), Next, CancellationToken.None);

        result.Should().Be("handled");
    }

    [Fact]
    public async Task Handle_InvalidRequest_ThrowsBeforeReachingHandler()
    {
        var behavior = new ValidationBehavior<SampleRequest, string>([new NameRequiredValidator()]);

        var act = () => behavior.Handle(new SampleRequest(""), Next, CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainSingle(e => e.PropertyName == nameof(SampleRequest.Name));
        _nextCalled.Should().BeFalse();
    }
}
