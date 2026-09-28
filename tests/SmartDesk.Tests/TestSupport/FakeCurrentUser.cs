using SmartDesk.Application.Common.Interfaces;

namespace SmartDesk.Tests.TestSupport;

public sealed class FakeCurrentUser(Guid? userId, params string[] roles) : ICurrentUserService
{
    public Guid? UserId { get; } = userId;

    public Guid? TeamId { get; init; }

    public bool IsInRole(string role) => roles.Contains(role);
}
