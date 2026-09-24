namespace SmartDesk.Application.Common.Interfaces;

// The authenticated caller of the current request, as read from the validated JWT.
public interface ICurrentUserService
{
    Guid? UserId { get; }

    bool IsInRole(string role);
}
