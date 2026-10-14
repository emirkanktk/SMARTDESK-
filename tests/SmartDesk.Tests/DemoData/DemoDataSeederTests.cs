using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SmartDesk.Application.Features.Reports.Queries.GetUsageReport;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Enums;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Infrastructure.Persistence;
using SmartDesk.Infrastructure.Persistence.Seed;
using SmartDesk.Infrastructure.Persistence.Seed.DemoData;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.DemoData;

// `--demo-data` on top of the Development seed (the plan's day 10), on real Identity and EF.
public sealed class DemoDataSeederTests : IDisposable
{
    // Wednesday 14 October 2026, 11:00 in Istanbul.
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    private readonly SqliteTestDatabase _database = new();
    private readonly ServiceProvider _services;

    public DemoDataSeederTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddScoped(_ => _database.CreateContext());
        services
            .AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<SmartDeskDbContext>();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        services.AddScoped<DemoResourceSeeder>();
        services.AddScoped<DemoDataSeeder>();
        _services = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        _database.Dispose();
    }

    // What the Development seed leaves (ADR-0007): roles, the four demo users, eight resources.
    private async Task DevelopmentSeedAsync()
    {
        using var scope = _services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in Roles.All)
        {
            await roles.CreateAsync(new IdentityRole<Guid>(role));
        }

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var demo in IdentityDataSeeder.DemoUsers)
        {
            var manager = demo.ManagerEmail is { } email ? await users.FindByEmailAsync(email) : null;
            var user = new ApplicationUser
            {
                UserName = demo.Email,
                Email = demo.Email,
                FirstName = demo.FirstName,
                LastName = demo.LastName,
                ManagerId = manager?.Id,
            };
            (await users.CreateAsync(user, IdentityDataSeeder.DemoPassword)).Succeeded.Should().BeTrue();
            await users.AddToRoleAsync(user, demo.Role);
        }

        await scope.ServiceProvider.GetRequiredService<DemoResourceSeeder>().SeedAsync();
    }

    private async Task<bool> DemoDataAsync()
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync();
    }

    [Fact]
    public async Task ItFillsAFreshDatabase_AndOnlyOnce()
    {
        await DevelopmentSeedAsync();

        (await DemoDataAsync()).Should().BeTrue();

        await using var db = _database.CreateContext();
        (await db.Users.CountAsync()).Should().Be(21);
        (await db.Resources.CountAsync()).Should().Be(DemoOffice.Places.Count);
        (await db.ReservationSeries.CountAsync()).Should().Be(2);
        (await db.IssueReports.CountAsync()).Should().Be(5);
        (await db.WaitlistEntries.CountAsync()).Should().Be(2);
        (await db.ResourceBlocks.CountAsync()).Should().Be(1);
        var bookings = await db.Reservations.CountAsync();
        bookings.Should().BeGreaterThan(500);

        var plans = await db.FloorPlans.ToListAsync();
        plans.Select(p => p.Location).Should().BeEquivalentTo(DemoOffice.SecondFloor, DemoOffice.ThirdFloor, DemoOffice.FifthFloor);
        plans.Should().OnlyContain(p => p.ContentType == "image/png" && p.Data.Take(4).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }));

        (await DemoDataAsync()).Should().BeFalse();
        (await db.Reservations.CountAsync()).Should().Be(bookings);
        (await db.Users.CountAsync()).Should().Be(21);
    }

    [Fact]
    public async Task ThePeopleAreInThreeTeams_WithPlainEmails_AndSignInWithTheDemoPassword()
    {
        await DevelopmentSeedAsync();
        await DemoDataAsync();

        using var scope = _services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var managers = await users.GetUsersInRoleAsync(Roles.Manager);
        managers.Should().HaveCount(3);
        var everyone = await users.Users.ToListAsync();
        managers.Select(m => everyone.Count(u => u.ManagerId == m.Id)).Should().BeEquivalentTo([6, 6, 5]);

        var newcomers = everyone.Where(u => !IdentityDataSeeder.DemoUsers.Any(d => d.Email == u.Email)).ToList();
        newcomers.Should().HaveCount(17).And.OnlyContain(u => System.Text.RegularExpressions.Regex.IsMatch(
            u.Email!, @"^[a-z]+\.[a-z]+\d*@smartdesk\.local$"));
        newcomers.Select(u => u.Email).Should().OnlyHaveUniqueItems();
        (await users.CheckPasswordAsync(newcomers[0], IdentityDataSeeder.DemoPassword)).Should().BeTrue();
    }

    [Fact]
    public async Task TheSeededResources_KeepWhatTheyHad_AndGetTheirPlace()
    {
        await DevelopmentSeedAsync();
        await DemoDataAsync();

        await using var db = _database.CreateContext();
        var resources = await db.Resources.Include(r => r.Features).Include(r => r.Teams).ToListAsync();
        var desk = resources.Single(r => r.Name == "Desk 3-01");
        desk.Features.Select(f => f.Name).Should().BeEquivalentTo("Monitor", "Docking Station");
        desk.CheckInRequired.Should().BeFalse();
        (desk.MapX, desk.MapY).Should().Be((0.1313, 0.15));
        resources.Single(r => r.Name == "Meeting Room B").Teams.Should().ContainSingle();
        resources.Single(r => r.Name == "Camera C-1").Status.Should().Be(ResourceStatus.Maintenance);
        resources.Single(r => r.Name == "Desk 3-05").CheckInRequired.Should().BeTrue();
        resources.Where(r => r.Location is DemoOffice.SecondFloor or DemoOffice.ThirdFloor or DemoOffice.FifthFloor)
            .Should().OnlyContain(r => r.MapX != null && r.MapY != null);
    }

    [Fact]
    public async Task TheUsageReport_HasAnOfficeToShow()
    {
        await DevelopmentSeedAsync();
        await DemoDataAsync();

        await using var db = _database.CreateContext();
        var report = await new GetUsageReportQueryHandler(db, new FakeIdentityService()).Handle(
            new GetUsageReportQuery(new DateOnly(2026, 9, 14), new DateOnly(2026, 10, 13), "Europe/Istanbul", null),
            CancellationToken.None);

        report.Totals.Utilization.Should().BeInRange(0.2, 0.8);
        report.Totals.NoShowRate.Should().BeInRange(0.05, 0.35);
        report.Totals.CancellationRate.Should().BeInRange(0.02, 0.15);
        var tuesday = report.HeatMap.Single(d => d.Day == DayOfWeek.Tuesday);
        var friday = report.HeatMap.Single(d => d.Day == DayOfWeek.Friday);
        tuesday.Hours[10].Should().BeGreaterThan(friday.Hours[10]);
        report.TopUsers.Should().HaveCount(5);
    }
}
