using Moq;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.ResourceBlocks.Commands.CreateResourceBlock;

namespace SmartDesk.Tests.TestSupport;

// The block handler for tests that only block: cancellations it makes (ADR-0038) are pushed
// nowhere unless a test passes the notifiers.
public static class TestBlocks
{
    public static CreateResourceBlockCommandHandler Handler(
        IApplicationDbContext db,
        ICurrentUserService user,
        IReservationNotifier? notifier = null,
        IInboxNotifier? inbox = null,
        FakeIdentityService? names = null) =>
        new(
            db,
            user,
            names ?? new FakeIdentityService(),
            notifier ?? Mock.Of<IReservationNotifier>(),
            TestInvitations.For(db, user, notifier: inbox, identity: names));
}
