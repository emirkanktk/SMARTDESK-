using Moq;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Approvals.Common;

namespace SmartDesk.Tests.TestSupport;

// Approvals (ADR-0049) for handler tests, on the test's clock: with no Admins to tell unless a
// test passes the accounts, and messages pushed nowhere. No seeded kind waits for approval, so
// most tests never reach it.
public static class TestApprovals
{
    public static BookingApprovals For(
        IApplicationDbContext db,
        TimeProvider? clock = null,
        IUserAccounts? accounts = null,
        IInboxNotifier? notifier = null)
    {
        var people = accounts ?? Mock.Of<IUserAccounts>();
        return new(
            db,
            people,
            TestInvitations.For(db, new FakeCurrentUser(null), people, notifier, clock),
            clock ?? TimeProvider.System);
    }
}
