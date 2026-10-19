using SmartDesk.Application.Common.Interfaces;

namespace SmartDesk.Tests.TestSupport;

public sealed class FakeCurrentUser(Guid? userId, params string[] roles) : ICurrentUserService
{
    public Guid? UserId { get; } = userId;

    public Guid? TeamId { get; init; }

    // False for a background job, which serves no request (ADR-0052).
    public bool InRequest { get; init; } = true;

    public bool IsInRole(string role) => roles.Contains(role);
}
