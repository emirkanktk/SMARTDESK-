using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SmartDesk.API.Authentication;
using SmartDesk.Application.Common.Security;

namespace SmartDesk.Tests.Auth;

// While an account must change its password, only the endpoints marked for it answer.
public class PasswordChangeGateTests
{
    private static async Task<(int Status, string Body)> RunAsync(bool mustChange, bool endpointAllows)
    {
        var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        app.UsePasswordChangeGate();
        app.Run(context =>
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
        if (mustChange)
        {
            claims.Add(new Claim(SmartDeskClaims.PasswordChangeRequired, "true"));
        }

        var context = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")),
        };
        context.Response.Body = new MemoryStream();
        var metadata = endpointAllows ? new EndpointMetadataCollection(new AllowWhilePasswordChangeRequiredAttribute()) : EndpointMetadataCollection.Empty;
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, metadata, "test"));

        await app.Build()(context);

        context.Response.Body.Position = 0;
        return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    [Fact]
    public async Task AnAccountThatMustChangeItsPassword_IsStoppedEverywhereElse_WithACode()
    {
        var (status, body) = await RunAsync(mustChange: true, endpointAllows: false);

        status.Should().Be(StatusCodes.Status403Forbidden);
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("code").GetString().Should().Be(PasswordChangeGate.Code);
        problem.RootElement.GetProperty("detail").GetString().Should().Be(PasswordChangeGate.Detail);
    }

    [Fact]
    public async Task TheMarkedEndpoints_StillAnswer()
    {
        (await RunAsync(mustChange: true, endpointAllows: true)).Status.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task EveryoneElse_PassesThrough()
    {
        (await RunAsync(mustChange: false, endpointAllows: false)).Status.Should().Be(StatusCodes.Status200OK);
    }
}
