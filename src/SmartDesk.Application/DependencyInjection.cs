using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SmartDesk.Application.Common.Behaviors;
using SmartDesk.Application.Features.Accounts.Common;
using SmartDesk.Application.Features.Meetings.Common;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Application.Resources;

namespace SmartDesk.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(AssemblyReference).Assembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssembly(typeof(AssemblyReference).Assembly);
        services.AddScoped<AccountCreation>();
        services.AddScoped<ResourceReader>();
        services.AddScoped<Invitees>();
        services.AddScoped<MeetingInvitations>();

        // FluentValidation's own messages ("'Name' must not be empty.") follow the UI culture,
        // which the API sets for every request from Accept-Language (ADR-0023). Field names in
        // them come from Messages too; the English ones are what FluentValidation would show
        // anyway ("Start Date Time"), so English messages read as they always have.
        ValidatorOptions.Global.LanguageManager.Culture = null;
        ValidatorOptions.Global.DisplayNameResolver = (_, member, _) =>
            member is null ? null : Messages.ResourceManager.GetString($"Property_{member.Name}");

        return services;
    }
}
