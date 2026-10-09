using FluentValidation;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Application.Resources;

namespace SmartDesk.Application.Features.Resources.Commands.SetResourcePhoto;

// Reported on "Photo", the form field the file comes in.
public sealed class SetResourcePhotoCommandValidator : AbstractValidator<SetResourcePhotoCommand>
{
    public SetResourcePhotoCommandValidator()
    {
        RuleFor(x => x.Data)
            .Cascade(CascadeMode.Stop)
            .Must(data => data.Length > 0)
            .WithMessage(_ => Messages.Resources_PhotoMissing)
            .Must(data => data.Length <= ResourcePhotoRules.MaxBytes)
            .WithMessage(_ => string.Format(Messages.Resources_PhotoTooLarge, ResourcePhotoRules.MaxBytes / (1024 * 1024)))
            .Must(data => ResourcePhotoRules.ReadSize(data) is not null)
            .WithMessage(_ => Messages.Resources_PhotoType)
            .Must(data => ResourcePhotoRules.ReadSize(data) is { Width: <= ResourcePhotoRules.MaxSide, Height: <= ResourcePhotoRules.MaxSide })
            .WithMessage(_ => string.Format(Messages.Resources_PhotoDimensions, ResourcePhotoRules.MaxSide))
            .OverridePropertyName("Photo");
    }
}
