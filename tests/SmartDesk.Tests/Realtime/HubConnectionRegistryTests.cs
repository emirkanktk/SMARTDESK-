using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using SmartDesk.API.Hubs;

namespace SmartDesk.Tests.Realtime;

// A change to an account cuts that user's hub connections, and only theirs.
public class HubConnectionRegistryTests
{
    [Fact]
    public void Ending_AUsersSessions_AbortsEachOfTheirConnections_AndNoOneElses()
    {
        var registry = new HubConnectionRegistry();
        var ayse = Guid.NewGuid();
        var mehmet = Guid.NewGuid();
        var tab1 = new FakeConnection("c1");
        var tab2 = new FakeConnection("c2");
        var other = new FakeConnection("c3");
        registry.Add(ayse, tab1);
        registry.Add(ayse, tab2);
        registry.Add(mehmet, other);

        registry.End(ayse);

        tab1.Aborted.Should().BeTrue();
        tab2.Aborted.Should().BeTrue();
        other.Aborted.Should().BeFalse();
        registry.CountFor(ayse).Should().Be(0);
        registry.CountFor(mehmet).Should().Be(1);
    }

    [Fact]
    public void AClosedConnection_IsForgotten()
    {
        var registry = new HubConnectionRegistry();
        var ayse = Guid.NewGuid();
        var tab = new FakeConnection("c1");
        registry.Add(ayse, tab);

        registry.Remove(ayse, "c1");

        registry.UsersConnected.Should().Be(0, "a user without connections isn't kept around");
        registry.End(ayse);
        tab.Aborted.Should().BeFalse();
        registry.CountFor(ayse).Should().Be(0);
    }

    private sealed class FakeConnection(string id) : HubCallerContext
    {
        public bool Aborted { get; private set; }

        public override string ConnectionId { get; } = id;
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User => null;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;

        public override void Abort() => Aborted = true;
    }
}
