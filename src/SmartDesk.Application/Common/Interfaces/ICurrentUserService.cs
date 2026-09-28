namespace SmartDesk.Application.Common.Interfaces;

// The authenticated caller of the current request, as read from the validated JWT.
public interface ICurrentUserService
{
    Guid? UserId { get; }

    // The caller's team, as the account is now (ADR-0020): a Manager's own id, an Employee's
    // manager's id, or null for an Admin or an Employee outside any team.
    Guid? TeamId { get; }

    bool IsInRole(string role);
}
