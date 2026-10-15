using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SmartDesk.Application.Common.Behaviors;
using SmartDesk.Application.Features.Accounts.Common;
using SmartDesk.Application.Features.Approvals.Common;
using SmartDesk.Application.Features.BookingRules.Common;
using SmartDesk.Application.Features.Issues.Common;
using SmartDesk.Application.Features.Meetings.Common;
using SmartDesk.Application.Features.Reservations.Commands.CreateReservation;
using SmartDesk.Application.Features.Reservations.Commands.UpdateReservation;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Application.Features.Waitlist.Offers;
using SmartDesk.Application.Resources;

namespace SmartDesk.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(AssemblyReference).Assembly);
            // Outermost: a request's freed times are offered to the waiting list only once it
            // has succeeded (ADR-0043).
            cfg.AddOpenBehavior(typeof(OfferFreedSlotsBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        services.AddScoped<FreedSlots>();

        services.AddValidatorsFromAssembly(typeof(AssemblyReference).Assembly);
        services.AddScoped<AccountCreation>();
        services.AddScoped<ResourceReader>();
        services.AddScoped<IssueReader>();
        services.AddScoped<Invitees>();
        services.AddScoped<MeetingInvitations>();

        // Every rule for what a booking is for, in one place (ADR-0046).
        services.AddScoped<BookingPolicy>();

        // Bookings that wait for an Admin's approval (ADR-0049).
        services.AddScoped<BookingApprovals>();

        // A booking or a move that clashes says what would do instead (ADR-0042). Registered
        // after MediatR's own, so it runs inside validation: a request that isn't valid never
        // gets this far.
        services.AddScoped<BookingSuggester>();
        services.AddTransient<IPipelineBehavior<CreateReservationCommand, ReservationDto>, BookingSuggestionsBehavior<CreateReservationCommand>>();
        services.AddTransient<IPipelineBehavior<UpdateReservationCommand, ReservationDto>, BookingSuggestionsBehavior<UpdateReservationCommand>>();

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
