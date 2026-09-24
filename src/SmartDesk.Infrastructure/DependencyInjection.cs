using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Infrastructure.Persistence;
using SmartDesk.Infrastructure.Persistence.Seed;

namespace SmartDesk.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<SmartDeskDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<SmartDeskDbContext>());

        // AddIdentityCore rather than AddIdentity: this is a bearer-token API, and AddIdentity
        // also registers cookie authentication, which turns a 403 into a 302 redirect to a
        // login page that doesn't exist.
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 8;
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<SmartDeskDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer) && !string.IsNullOrWhiteSpace(o.Audience),
                "Jwt:Issuer and Jwt:Audience must be set.")
            .Validate(o => Encoding.UTF8.GetByteCount(o.Key) >= JwtOptions.MinimumKeyBytes,
                $"Jwt:Key must be at least {JwtOptions.MinimumKeyBytes} bytes. Set it with: " +
                "dotnet user-secrets set \"Jwt:Key\" \"<a long random string>\" --project src/SmartDesk.API")
            .Validate(o => o.AccessTokenLifetimeMinutes > 0, "Jwt:AccessTokenLifetimeMinutes must be positive.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IdentityDataSeeder>();
        services.AddScoped<DemoResourceSeeder>();

        return services;
    }

    // Roles in every environment; demo users and demo resources only when includeDemoData
    // (Development - ADR-0007). Resource types come from the migration, not from here.
    public static async Task SeedDatabaseAsync(this IServiceProvider services, bool includeDemoData)
    {
        await using var scope = services.CreateAsyncScope();

        var identitySeeded = await scope.ServiceProvider.GetRequiredService<IdentityDataSeeder>().SeedAsync(includeDemoData);
        if (identitySeeded && includeDemoData)
        {
            await scope.ServiceProvider.GetRequiredService<DemoResourceSeeder>().SeedAsync();
        }
    }
}
