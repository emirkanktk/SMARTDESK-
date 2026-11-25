using Moq;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Auth.Common;

namespace SmartDesk.Tests.TestSupport;

// A SessionIssuer for the handlers' own tests, over their mocks. Every browser is one the account
// knows, so nobody is told of a sign-in (ADR-0062): that has tests of its own, on the real thing.
public static class SessionIssuers
{
    public const string DeviceToken = "known-device-token";

    public static SessionIssuer Over(
        IIdentityService identity, IJwtTokenGenerator tokens, IUserSessions sessions, ICurrentUserService currentUser)
    {
        var devices = new Mock<IKnownDevices>();
        devices
            .Setup(d => d.SignedInAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeviceCheck(DeviceFamiliarity.Known, DeviceToken));

        return new SessionIssuer(
            identity,
            tokens,
            sessions,
            devices.Object,
            currentUser,
            Mock.Of<IApplicationDbContext>(),
            new CapturingEmailSender(),
            new CapturingPushSender(),
            Mock.Of<IFrontendLinks>(),
            TimeProvider.System);
    }
}
