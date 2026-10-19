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
}
