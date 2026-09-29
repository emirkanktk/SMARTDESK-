using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartDesk.Application;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Common.Security;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Enums;
using SmartDesk.Infrastructure;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Infrastructure.Persistence;

namespace SmartDesk.Tests.TestSupport;

// The account features end to end below the API: requests go through MediatR with the real
// validators, onto real Identity (SQLite, data protection, token providers), UserAccounts and
// UserAccessChecker. Emails are captured instead of sent.
public sealed class AccountsTestHost : IDisposable
{
    public const string Password = "SmartDesk123!";
    public const string FrontendUrl = "http://localhost:4200";

    private readonly SqliteTestDatabase _database = new();
    private readonly SwitchableCurrentUser _currentUser = new();
    private readonly ServiceProvider _services;

    public AccountsTestHost()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddHttpContextAccessor();
        services.AddAuthentication();
        services.AddMemoryCache();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped(_ => _database.CreateContext());
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<SmartDeskDbContext>());
        services
            .AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<IdentityRole<Guid>>()
            .AddErrorDescriber<LocalizedIdentityErrorDescriber>()
            .AddEntityFrameworkStores<SmartDeskDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();
        services.AddApplication();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IUserAccounts, UserAccounts>();
        services.AddScoped<IUserAccessChecker, UserAccessChecker>();
        services.AddScoped<UserAccessCache>();
        services.AddSingleton<IRealtimeSessions>(RealtimeSessions);
        services.AddSingleton<ICurrentUserService>(_currentUser);
        services.AddSingleton<IEmailSender>(Emails);
        services.AddSingleton<IFrontendLinks>(new FrontendLinks(Options.Create(new AppOptions { FrontendUrl = FrontendUrl })));
        services.AddSingleton<IJwtTokenGenerator>(new JwtTokenGenerator(
            Options.Create(new JwtOptions { Issuer = "SmartDesk", Audience = "SmartDesk", Key = new string('k', 64), AccessTokenLifetimeMinutes = 60 }),
            TimeProvider.System));
        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in Roles.All)
        {
            roles.CreateAsync(new IdentityRole<Guid>(role)).GetAwaiter().GetResult();
        }
    }

    public CapturingEmailSender Emails { get; } = new();

    public RecordingRealtimeSessions RealtimeSessions { get; } = new();

    public SqliteTestDatabase Database => _database;

    public void Dispose()
    {
        _services.Dispose();
        _database.Dispose();
    }

    public async Task<Guid> AddUserAsync(
        string email,
        string role,
        AccountStatus status = AccountStatus.Active,
        Guid? managerId = null,
        string firstName = "Test",
        string lastName = "User",
        string? password = Password,
        bool mustChangePassword = false,
        string? language = null)
    {
        await using var scope = _services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            Status = status,
            ManagerId = managerId,
            MustChangePassword = mustChangePassword,
            Language = language,
        };
        var created = password is null ? await users.CreateAsync(user) : await users.CreateAsync(user, password);
        Succeeded(created, $"create {email}");
        Succeeded(await users.AddToRoleAsync(user, role), $"add {email} to {role}");
        return user.Id;
    }

    // Sends the request as this caller, with the roles their token would carry and the team
    // their account is in now, as JwtBearerSetup would add it.
    public async Task<TResponse> SendAsync<TResponse>(Guid? callerId, string callerRole, IRequest<TResponse> request)
    {
        await using var scope = _services.CreateAsyncScope();
        await SetCallerAsync(scope.ServiceProvider, callerId, callerRole);
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    public async Task SendAsync(Guid? callerId, string callerRole, IRequest request)
    {
        await using var scope = _services.CreateAsyncScope();
        await SetCallerAsync(scope.ServiceProvider, callerId, callerRole);
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    private async Task SetCallerAsync(IServiceProvider services, Guid? callerId, string callerRole)
    {
        Guid? managerId = null;
        if (callerId is { } id)
        {
            var user = await services.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(id.ToString());
            managerId = user?.ManagerId;
        }

        _currentUser.Set(callerId, callerRole, callerId is { } caller ? Teams.TeamOf(caller, [callerRole], managerId) : null);
    }

    // An anonymous request (sign-up, sign-in).
    public Task<TResponse> SendAnonymousAsync<TResponse>(IRequest<TResponse> request) => SendAsync(null, string.Empty, request);

    public Task SendAnonymousAsync(IRequest request) => SendAsync(null, string.Empty, request);

    public async Task<T> WithAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    public Task<UserAccount?> FindAsync(Guid id) =>
        WithAsync(sp => sp.GetRequiredService<IUserAccounts>().FindAsync(id, CancellationToken.None));

    public async Task<UserAccount?> FindByEmailAsync(string email)
    {
        var id = await WithAsync(async sp =>
            (await sp.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))?.Id);
        return id is { } found ? await FindAsync(found) : null;
    }

    // Would a token for this user with these roles still be accepted? The same test as
    // JwtBearerSetup, minus the session stamp.
    public async Task<bool> IsCurrentAsync(Guid userId, params string[] tokenRoles)
    {
        var access = await GetAccessAsync(userId);
        return access is { Status: AccountStatus.Active } && access.Roles.SetEquals(tokenRoles);
    }

    public Task<UserAccessSnapshot?> GetAccessAsync(Guid userId) =>
        WithAsync(sp => sp.GetRequiredService<IUserAccessChecker>().GetAsync(userId, CancellationToken.None));

    private static void Succeeded(IdentityResult result, string what)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Could not {what}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }
    }

    private sealed class SwitchableCurrentUser : ICurrentUserService
    {
        private string _role = string.Empty;

        public Guid? UserId { get; private set; }

        public Guid? TeamId { get; private set; }

        public bool IsInRole(string role) => UserId is not null && role == _role;

        public void Set(Guid? userId, string role, Guid? teamId)
        {
            UserId = userId;
            _role = role;
            TeamId = teamId;
        }
    }

    public sealed class RecordingRealtimeSessions : IRealtimeSessions
    {
        public List<Guid> Ended { get; } = [];

        public void End(Guid userId) => Ended.Add(userId);
    }
}
