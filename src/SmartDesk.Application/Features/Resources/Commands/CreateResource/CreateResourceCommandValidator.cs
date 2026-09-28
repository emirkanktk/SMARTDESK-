using FluentValidation;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Resources.Common;

namespace SmartDesk.Application.Features.Resources.Commands.CreateResource;

public sealed class CreateResourceCommandValidator : ResourceFieldsValidator<CreateResourceCommand>
{
    public CreateResourceCommandValidator(IApplicationDbContext db)
        : base(db)
    {
        RuleFor(x => x.TeamIds).TeamList();
    }
}
