using FluentValidation;
using SmartDesk.Application.Features.Accounts.Common;

namespace SmartDesk.Application.Features.Auth.Commands.UpdateProfile;

// The same rules as signing up.
public sealed class UpdateProfileCommandValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileCommandValidator()
    {
        RuleFor(x => x.FirstName).PersonName();
        RuleFor(x => x.LastName).PersonName();
    }
}
