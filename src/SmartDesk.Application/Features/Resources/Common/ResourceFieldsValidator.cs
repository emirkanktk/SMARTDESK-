using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Interfaces;

namespace SmartDesk.Application.Features.Resources.Common;

// Limits match the column lengths in SmartDeskDbContext, so a request that passes here can't
// fail later with a database truncation error.
public abstract class ResourceFieldsValidator<T> : AbstractValidator<T>
    where T : IResourceFields
{
    public const int MaxCapacity = 1000;
    public const int MaxFeatures = 20;

    protected ResourceFieldsValidator(IApplicationDbContext db)
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Location).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.Capacity).InclusiveBetween(1, MaxCapacity);

        RuleFor(x => x.ResourceTypeId)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MustAsync((id, ct) => db.ResourceTypes.AnyAsync(t => t.Id == id, ct))
            .WithMessage("Resource type does not exist.");

        RuleFor(x => x.Features)
            .Must(f => f is null || f.Count <= MaxFeatures)
            .WithMessage($"A resource can have at most {MaxFeatures} features.")
            .Must(f => f is null || ResourceFeatureNames.HasNoDuplicates(f))
            .WithMessage("Features must not contain duplicates.");

        RuleForEach(x => x.Features).NotEmpty().MaximumLength(100);
    }
}
