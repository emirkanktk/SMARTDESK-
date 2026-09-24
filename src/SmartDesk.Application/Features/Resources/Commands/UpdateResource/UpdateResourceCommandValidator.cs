using FluentValidation;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Resources.Common;

namespace SmartDesk.Application.Features.Resources.Commands.UpdateResource;

public sealed class UpdateResourceCommandValidator : ResourceFieldsValidator<UpdateResourceCommand>
{
    public UpdateResourceCommandValidator(IApplicationDbContext db) : base(db)
    {
        RuleFor(x => x.Status).IsInEnum();
    }
}
