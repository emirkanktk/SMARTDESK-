using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SmartDesk.Domain.Constants;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Infrastructure.Persistence;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Infrastructure;

// Real Identity (UserManager, SignInManager, lockout) on the SQLite test database. Timing
// itself is too noisy to assert on, so these count password verifications instead: every
// failed sign-in must do the same expensive check, whether or not the account exists.
public sealed class IdentityServiceTests : IDisposable
{
    private const string Email = "employee1@smartdesk.local";
    private const string Password = "SmartDesk123!";

    private readonly SqliteTestDatabase _database = new();
    private readonly CountingPasswordHasher _hasher = new();
    private readonly ServiceProvider _services;

    public IdentityServiceTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddAuthentication();
        services.AddScoped(_ => _database.CreateContext());
        services
            .AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<SmartDeskDbContext>()
            .AddSignInManager();
        services.AddSingleton<IPasswordHasher<ApplicationUser>>(_hasher);
        services.AddScoped<IdentityService>();
        _services = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        _database.Dispose();
    }

    private async Task<ApplicationUser> AddUserAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        await roles.CreateAsync(new IdentityRole<Guid>(Roles.Employee));

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = Email, Email = Email, FirstName = "Ayşe", LastName = "Yılmaz" };
        (await users.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
        await users.AddToRoleAsync(user, Roles.Employee);
        return user;
    }

    private async Task<(bool SignedIn, int Verifications)> SignInAsync(string email, string password)
    {
        await using var scope = _services.CreateAsyncScope();
        var before = _hasher.Verifications;
        var profile = await scope.ServiceProvider.GetRequiredService<IdentityService>()
            .CheckCredentialsAsync(email, password, CancellationToken.None);
        return (profile is not null, _hasher.Verifications - before);
    }

    [Fact]
    public async Task CheckCredentials_RightPassword_ReturnsTheProfileWithRoles()
    {
        await AddUserAsync();

        await using var scope = _services.CreateAsyncScope();
        var profile = await scope.ServiceProvider.GetRequiredService<IdentityService>()
            .CheckCredentialsAsync(Email, Password, CancellationToken.None);

        profile.Should().NotBeNull();
        profile!.Email.Should().Be(Email);
        profile.Roles.Should().Equal(Roles.Employee);
    }

    [Fact]
    public async Task CheckCredentials_UnknownEmail_ChecksAPasswordLikeAWrongPasswordDoes()
    {
        await AddUserAsync();

        var wrongPassword = await SignInAsync(Email, "Wrong-password-1");
        var unknownEmail = await SignInAsync("nobody@smartdesk.local", "Wrong-password-1");

        wrongPassword.Should().Be((false, 1));
        unknownEmail.Should().Be((false, 1));
    }

    [Fact]
    public async Task CheckCredentials_LockedOutAccount_StillChecksAPassword()
    {
        var user = await AddUserAsync();
        await using (var scope = _services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var stored = await users.FindByIdAsync(user.Id.ToString());
            await users.SetLockoutEndDateAsync(stored!, DateTimeOffset.UtcNow.AddMinutes(5));
        }

        // Identity refuses a locked-out account before looking at the password, even the right one.
        (await SignInAsync(Email, Password)).Should().Be((false, 1));
    }

    [Fact]
    public async Task CheckCredentials_AccountWithNoPasswordYet_StillChecksAPassword()
    {
        // An invited account whose owner hasn't chosen a password (ADR-0020): Identity has no
        // hash to check against, so it would answer faster than for a wrong password.
        await using (var scope = _services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            (await users.CreateAsync(new ApplicationUser { UserName = Email, Email = Email })).Succeeded.Should().BeTrue();
        }

        (await SignInAsync(Email, "Any-password-1")).Should().Be((false, 1));
    }

    private sealed class CountingPasswordHasher : PasswordHasher<ApplicationUser>
    {
        private int _verifications;

        public int Verifications => _verifications;

        public override PasswordVerificationResult VerifyHashedPassword(ApplicationUser user, string hashedPassword, string providedPassword)
        {
            Interlocked.Increment(ref _verifications);
            return base.VerifyHashedPassword(user, hashedPassword, providedPassword);
        }
    }
}
