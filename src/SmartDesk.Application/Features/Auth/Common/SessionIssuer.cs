using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Localization;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Auth.Commands.Login;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Auth.Common;

// An access token under a session, and the answer that carries both (ADR-0026).
public static class SessionIssuer
{
    public static SignedIn Answer(IJwtTokenGenerator tokens, UserProfile user, IssuedSession session)
    {
        var token = tokens.Generate(user, session.SessionId);
        return new SignedIn(
            new LoginResponse(token.Token, token.ExpiresAtUtc, user.MustChangePassword),
            session.RefreshToken,
            session.ExpiresAt);
    }

    // Signing in, once a password or a passkey (ADR-0057) has said whose account it is.
    public static async Task<SignedIn> SignInAsync(
        UserProfile user,
        IIdentityService identityService,
        IJwtTokenGenerator tokens,
        IUserSessions sessions,
        CancellationToken cancellationToken)
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

        var session = await sessions.StartAsync(user.Id, user.SessionStamp, cancellationToken);
        return Answer(tokens, user, session);
    }
}
