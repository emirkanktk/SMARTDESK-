using System.Globalization;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SmartDesk.Application.Common.Behaviors;

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

        // FluentValidation localizes its default messages to the machine's UI culture, so the
        // same API would answer in Turkish on one developer's machine and English on another.
        // Pin it: the API contract is English, the frontend owns any translation.
        ValidatorOptions.Global.LanguageManager.Culture = CultureInfo.GetCultureInfo("en-US");

        return services;
    }
}
