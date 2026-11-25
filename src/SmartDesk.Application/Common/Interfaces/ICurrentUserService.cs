namespace SmartDesk.Application.Common.Interfaces;

// The authenticated caller of the current request, as read from the validated JWT.
public interface ICurrentUserService
{
    Guid? UserId { get; }

    // The caller's team, as the account is now (ADR-0020): a Manager's own id, an Employee's
    // manager's id, or null for an Admin or an Employee outside any team.
    Guid? TeamId { get; }

    bool IsInRole(string role);

    // Whether a request is being served at all: false in the background jobs, which change
    // things on nobody's behalf. A request without a UserId is someone without an account
    // (ADR-0052).
    bool InRequest => true;

    // The sign-in session the caller's token was issued under (ADR-0026).
    Guid? SessionId => null;

    // The calling browser's description of itself, its User-Agent header as sent; a session
    // notes it when it starts (ADR-0061). Null outside a request, or when there's none.
    string? UserAgent => null;

    // The token the calling browser is known by to the accounts that signed in on it, from its
    // cookie (ADR-0062). Null when it has none, or outside a request.
    string? DeviceToken => null;
}
