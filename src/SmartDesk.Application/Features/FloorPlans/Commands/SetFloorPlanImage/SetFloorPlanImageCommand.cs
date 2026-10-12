using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.FloorPlans.Common;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.FloorPlans.Commands.SetFloorPlanImage;

// Sets or replaces a floor's plan (PUT /api/floor-plans/image?location=, multipart). Admins
// only: a floor is everyone's (ADR-0041). The location needn't have resources yet.
public sealed record SetFloorPlanImageCommand(string Location, byte[] Data) : IRequest<FloorDto>;

// A plan follows a photo's rules (ADR-0027), reported on "Plan", the form field it comes in.
public sealed class SetFloorPlanImageCommandValidator : AbstractValidator<SetFloorPlanImageCommand>
{
    public SetFloorPlanImageCommandValidator()
    {
        RuleFor(x => x.Location)
            .Must(location => !string.IsNullOrWhiteSpace(location))
            .WithMessage(_ => Messages.FloorPlans_LocationMissing)
            .MaximumLength(200);

        RuleFor(x => x.Data)
            .Cascade(CascadeMode.Stop)
            .Must(data => data.Length > 0)
            .WithMessage(_ => Messages.FloorPlans_Missing)
            .Must(data => data.Length <= ResourcePhotoRules.MaxBytes)
            .WithMessage(_ => string.Format(Messages.FloorPlans_TooLarge, ResourcePhotoRules.MaxBytes / (1024 * 1024)))
            .Must(data => ResourcePhotoRules.ReadSize(data) is not null)
            .WithMessage(_ => Messages.FloorPlans_Type)
            .Must(data => ResourcePhotoRules.ReadSize(data) is { Width: <= ResourcePhotoRules.MaxSide, Height: <= ResourcePhotoRules.MaxSide })
            .WithMessage(_ => string.Format(Messages.FloorPlans_Dimensions, ResourcePhotoRules.MaxSide))
            .OverridePropertyName("Plan");
    }
}

public sealed class SetFloorPlanImageCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    TimeProvider timeProvider) : IRequestHandler<SetFloorPlanImageCommand, FloorDto>
{
    public async Task<FloorDto> Handle(SetFloorPlanImageCommand request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsInRole(Roles.Admin))
        {
            throw new ForbiddenAccessException(Messages.FloorPlans_AdminsOnly);
        }

        var location = request.Location.Trim();
        var plan = await db.FloorPlans.SingleOrDefaultAsync(p => p.Location == location, cancellationToken);
        if (plan is null)
        {
            plan = new FloorPlan { Location = location };
            db.FloorPlans.Add(plan);
        }

        var (width, height) = ResourcePhotoRules.ReadSize(request.Data)!.Value;
        plan.ContentType = ResourcePhotoRules.DetectContentType(request.Data)!;
        plan.Data = request.Data;
        plan.Width = width;
        plan.Height = height;
        plan.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);

        // An Admin may use every resource, so the floor's counts are all of them.
        var resources = db.Resources.AsNoTracking().Where(r => r.Location == location);
        return new FloorDto(
            location,
            await resources.CountAsync(cancellationToken),
            await resources.CountAsync(r => r.MapX != null, cancellationToken),
            new FloorPlanImageDto(width, height, plan.UpdatedAt));
    }
}
