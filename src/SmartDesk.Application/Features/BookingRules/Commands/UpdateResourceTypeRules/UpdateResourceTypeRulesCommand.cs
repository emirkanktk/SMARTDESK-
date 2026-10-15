using System.Text.Json.Serialization;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.BookingRules.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.BookingRules.Commands.UpdateResourceTypeRules;

// An Admin sets how a kind of resource may be booked (ADR-0046); null leaves a rule unset. As for
// the office's hours, bookings already made stay as they are: switching approval on (ADR-0049)
// asks it of bookings made or moved from then on.
public sealed record UpdateResourceTypeRulesCommand(
    int? MaxDurationMinutes,
    int? MaxDaysAhead,
    int? MaxActivePerUser,
    bool OfficeHoursOnly,
    bool RequiresApproval = false) : IRequest<ResourceTypeRulesDto>
{
    // Comes from the route (PUT /api/booking-rules/types/{id}), not the body.
    [JsonIgnore]
    public Guid Id { get; init; }
}

public sealed class UpdateResourceTypeRulesCommandValidator : AbstractValidator<UpdateResourceTypeRulesCommand>
{
    public UpdateResourceTypeRulesCommandValidator()
    {
        RuleFor(x => x.MaxDurationMinutes)
            .Must(RuleBounds.IsDuration)
            .WithMessage(_ => string.Format(Messages.TypeRules_MaxDuration, RuleBounds.MaxDurationMinutes / (24 * 60)));

        RuleFor(x => x.MaxDaysAhead)
            .Must(RuleBounds.IsDaysAhead)
            .WithMessage(_ => string.Format(Messages.TypeRules_MaxDaysAhead, RuleBounds.MaxDaysAhead));

        RuleFor(x => x.MaxActivePerUser)
            .Must(RuleBounds.IsActiveLimit)
            .WithMessage(_ => string.Format(Messages.TypeRules_MaxActive, RuleBounds.MaxActivePerUser));
    }
}

public sealed class UpdateResourceTypeRulesCommandHandler(IApplicationDbContext db)
    : IRequestHandler<UpdateResourceTypeRulesCommand, ResourceTypeRulesDto>
{
    public async Task<ResourceTypeRulesDto> Handle(UpdateResourceTypeRulesCommand request, CancellationToken cancellationToken)
    {
        var type = await db.ResourceTypes.SingleOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(ResourceType), request.Id);

        type.MaxDurationMinutes = request.MaxDurationMinutes;
        type.MaxDaysAhead = request.MaxDaysAhead;
        type.MaxActivePerUser = request.MaxActivePerUser;
        type.OfficeHoursOnly = request.OfficeHoursOnly;
        type.RequiresApproval = request.RequiresApproval;
        await db.SaveChangesAsync(cancellationToken);

        return ResourceTypeRulesDto.From(type);
    }
}
