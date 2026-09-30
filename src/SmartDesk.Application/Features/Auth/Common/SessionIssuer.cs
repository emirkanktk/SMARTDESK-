using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Auth.Commands.Login;

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
}
