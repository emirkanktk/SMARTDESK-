using Moq;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Meetings.Common;

namespace SmartDesk.Tests.TestSupport;

// Meeting invitations (ADR-0032) for handler tests: nobody to invite unless a test passes the
// accounts, and messages pushed nowhere unless it passes a notifier.
public static class TestInvitations
{
    public static MeetingInvitations For(
        IApplicationDbContext db,
        ICurrentUserService user,
        IUserAccounts? accounts = null,
        IInboxNotifier? notifier = null,
        TimeProvider? clock = null,
        IIdentityService? identity = null) =>
        new(
            db,
            new Invitees(accounts ?? Mock.Of<IUserAccounts>(), user),
            user,
            identity ?? new FakeIdentityService(),
            notifier ?? Mock.Of<IInboxNotifier>(),
            clock ?? TimeProvider.System);
}
