using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartDesk.Domain.Constants;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Infrastructure.Persistence;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Infrastructure;

// Real Identity on the SQLite test database, with the roles already seeded as on startup.
public sealed class BootstrapAdminSeederTests : IDisposable
{
    private const string Email = "it-admin@company.example";
    private const string Password = "Bootstrap-Pass-1";

    private readonly SqliteTestDatabase _database = new();
    private readonly ServiceProvider _services;
    private readonly ListLogger _logger = new();

    public BootstrapAdminSeederTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => _database.CreateContext());
        services
            .AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<SmartDeskDbContext>();
        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in new[] { Roles.Admin, Roles.Employee })
        {
            roles.CreateAsync(new IdentityRole<Guid>(role)).GetAwaiter().GetResult();
        }
    }

    public void Dispose()
    {
        _services.Dispose();
        _database.Dispose();
    }

    private async Task SeedAsync(string email = Email, string password = Password)
    {
        await using var scope = _services.CreateAsyncScope();
        var seeder = new BootstrapAdminSeeder(
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
            Options.Create(new BootstrapAdminOptions { Email = email, Password = password }),
            _logger);
        await seeder.SeedAsync();
    }

    private async Task<T> WithUsersAsync<T>(Func<UserManager<ApplicationUser>, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>());
    }

    [Fact]
    public async Task Configured_CreatesTheAdminWhoCanSignIn()
    {
        await SeedAsync();

        var (isAdmin, passwordWorks) = await WithUsersAsync(async users =>
        {
            var user = await users.FindByEmailAsync(Email);
            return (await users.IsInRoleAsync(user!, Roles.Admin), await users.CheckPasswordAsync(user!, Password));
        });
        isAdmin.Should().BeTrue();
        passwordWorks.Should().BeTrue();
    }

    [Fact]
    public async Task RunTwice_KeepsOneAccount()
    {
        await SeedAsync();
        await SeedAsync();

        (await WithUsersAsync(users => users.GetUsersInRoleAsync(Roles.Admin))).Should().ContainSingle();
    }

    [Fact]
    public async Task ExistingAccount_BecomesAdmin_AndKeepsItsPassword()
    {
        await WithUsersAsync(async users =>
        {
            var user = new ApplicationUser { UserName = Email, Email = Email, FirstName = "Ayşe", LastName = "Yılmaz" };
            await users.CreateAsync(user, "Her-Own-Pass-1");
            return await users.AddToRoleAsync(user, Roles.Employee);
        });

        await SeedAsync();

        var (isAdmin, ownPassword, bootstrapPassword) = await WithUsersAsync(async users =>
        {
            var user = await users.FindByEmailAsync(Email);
            return (await users.IsInRoleAsync(user!, Roles.Admin),
                await users.CheckPasswordAsync(user!, "Her-Own-Pass-1"),
                await users.CheckPasswordAsync(user!, Password));
        });
        isAdmin.Should().BeTrue();
        ownPassword.Should().BeTrue();
        bootstrapPassword.Should().BeFalse();
    }

    [Fact]
    public async Task APasswordIdentityRejects_FailsStartupWithItsReason()
    {
        var act = () => SeedAsync(password: "short");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*bootstrap admin*Passwords must be at least*");
    }

    [Fact]
    public async Task NotConfigured_CreatesNobody_AndWarnsWhenThereIsNoAdmin()
    {
        await SeedAsync(email: "", password: "");

        (await WithUsersAsync(users => Task.FromResult(users.Users.Count()))).Should().Be(0);
        _logger.Warnings.Should().ContainSingle().Which.Should().Contain("No admin account exists");
    }

    [Theory]
    [InlineData("", "", true)]
    [InlineData(Email, Password, true)]
    [InlineData(Email, "", false)]
    [InlineData("", Password, false)]
    public void Options_EmailAndPasswordComeTogether(string email, string password, bool complete) =>
        new BootstrapAdminOptions { Email = email, Password = password }.IsComplete.Should().Be(complete);

    private sealed class ListLogger : ILogger<BootstrapAdminSeeder>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
