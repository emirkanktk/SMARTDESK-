using FluentAssertions;
using SmartDesk.Application.Common.Extensions;

namespace SmartDesk.Tests.Common;

public class DateTimeExtensionsTests
{
    private static readonly DateTime Utc = new(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AsUtc_Utc_IsUnchanged() => Utc.AsUtc().Should().Be(Utc).And.BeIn(DateTimeKind.Utc);

    [Fact]
    public void AsUtc_Local_ConvertsToTheSameInstant() =>
        Utc.ToLocalTime().AsUtc().Should().Be(Utc).And.BeIn(DateTimeKind.Utc);

    [Fact]
    public void AsUtc_Unspecified_IsTakenAsUtc() =>
        DateTime.SpecifyKind(Utc, DateTimeKind.Unspecified).AsUtc().Should().Be(Utc).And.BeIn(DateTimeKind.Utc);
}
