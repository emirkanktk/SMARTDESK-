using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Localization;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Auth.Commands.Login;
using SmartDesk.Application.Features.Push.Common;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Auth.Common;

// An access token under a session, and the answer that carries both (ADR-0026). Signing in also
// notes the browser, and tells the account's owner when it's one their account hadn't been used
// on (ADR-0062).
public sealed class SessionIssuer(
    IIdentityService identityService,
    IJwtTokenGenerator tokens,
    IUserSessions sessions,
    IKnownDevices devices,
    ICurrentUserService currentUser,
    IApplicationDbContext db,
    IEmailSender emails,
    IPushSender push,
    IFrontendLinks links,
    TimeProvider timeProvider)
{
    public static SignedIn Answer(IJwtTokenGenerator tokens, UserProfile user, IssuedSession session)
    {
        var token = tokens.Generate(user, session.SessionId);
        return new SignedIn(
            new LoginResponse(token.Token, token.ExpiresAtUtc, user.MustChangePassword),
            session.RefreshToken,
            session.ExpiresAt);
    }

    // Signing in, once a password or a passkey (ADR-0057) has said whose account it is. The
    // session notes which, and on what browser (ADR-0061); the browser keeps a token of its own
    // that says it's one the account knows from now on (ADR-0062).
    public async Task<SignedIn> SignInAsync(UserProfile user, SignInMethod method, CancellationToken cancellationToken)
    {
        // Only once the password or passkey matched, so these say nothing to someone guessing.
        switch (user.Status)
        {
            case AccountStatus.PendingApproval:
                throw new ForbiddenAccessException(LoginCommandHandler.PendingApprovalMessage);
            case AccountStatus.Disabled:
                throw new ForbiddenAccessException(LoginCommandHandler.DisabledMessage);
        }

        if (user.Language != Languages.Current)
        {
            await identityService.RememberLanguageAsync(user.Id, Languages.Current, cancellationToken);
        }

        var origin = new SessionOrigin(method, currentUser.UserAgent);
        var device = await devices.SignedInAsync(user.Id, currentUser.DeviceToken, cancellationToken);
        var session = await sessions.StartAsync(user.Id, user.SessionStamp, origin, cancellationToken);
        if (device.Familiarity == DeviceFamiliarity.New)
        {
            var signIn = new NewSignIn(session.SessionId, origin.UserAgent, method, timeProvider.GetUtcNow().UtcDateTime);
            await TellOwnerAsync(user, signIn, cancellationToken);
        }

        return Answer(tokens, user, session) with { DeviceToken = device.Token };
    }

    // By email, which reaches them even while someone else is in their account, and on the
    // devices they turned notifications on in (ADR-0056). The email is in the language their
    // emails were in before this sign-in, which makes the browser's theirs (ADR-0023): whoever
    // signed in may not be them. Both only queue, so the sign-in doesn't wait on either.
    private async Task TellOwnerAsync(UserProfile user, NewSignIn signIn, CancellationToken cancellationToken)
    {
        var zoneId = await db.OfficeHours.AsNoTracking().Select(h => h.TimeZone).FirstOrDefaultAsync(cancellationToken);
        var zone = zoneId is not null && TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out var found) ? found : TimeZoneInfo.Utc;

        emails.Queue(AccountEmails.NewSignIn(Languages.CultureFor(user.Language), user, signIn, zone, links.SignedInDevices()));
        push.Queue(new PushNotice(PushTopic.NewSignIn, [user.Id]) { SignIn = signIn });
    }
}
