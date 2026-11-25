using SmartDesk.Application.Common.Interfaces;

namespace SmartDesk.Tests.TestSupport;

public sealed class FakeCurrentUser(Guid? userId, params string[] roles) : ICurrentUserService
{
    public Guid? UserId { get; } = userId;

    public Guid? TeamId { get; init; }

    // False for a background job, which serves no request (ADR-0052).
    public bool InRequest { get; init; } = true;

    public Guid? SessionId { get; init; }

    public string? UserAgent { get; init; }

    public string? DeviceToken { get; init; }

    public bool IsInRole(string role) => roles.Contains(role);
}
