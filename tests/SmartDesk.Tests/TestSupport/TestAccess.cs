using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Domain.Constants;

namespace SmartDesk.Tests.TestSupport;

// For handler tests that aren't about who may use what (ADR-0022): an Admin sees and manages
// every resource, as everyone did before teams existed.
public static class TestAccess
{
    public static FakeCurrentUser Admin { get; } = new(Guid.NewGuid(), Roles.Admin);

    public static ResourceReader Reader(FakeIdentityService? names = null) => new(names ?? new FakeIdentityService());
}
