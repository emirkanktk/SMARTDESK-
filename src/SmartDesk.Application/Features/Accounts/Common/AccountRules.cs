using FluentValidation;
using FluentValidation.Results;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Accounts.Common;

public static class AccountRules
{
    public const int MaxNameLength = 100;
    public const int MaxEmailLength = 256;

    public static string EmailTaken => Messages.Accounts_EmailTaken;

    // Names go into email subjects and greetings, so no line breaks or other control characters.
    public static IRuleBuilderOptions<T, string> PersonName<T>(this IRuleBuilderInitial<T, string> rule) =>
        rule.Cascade(CascadeMode.Stop)
            .NotEmpty()
            .MaximumLength(MaxNameLength)
            .Must(name => !name.Any(char.IsControl)).WithMessage(_ => Messages.Validation_NoControlCharacters);

    public static IRuleBuilderOptions<T, string> AccountEmail<T>(this IRuleBuilderInitial<T, string> rule) =>
        rule.Cascade(CascadeMode.Stop).NotEmpty().EmailAddress().MaximumLength(MaxEmailLength);

    // Identity's own rules (length, character classes) are applied when the password is set;
    // this only keeps the request itself sane.
    public static IRuleBuilderOptions<T, string> TemporaryPassword<T>(this IRuleBuilderInitial<T, string> rule) =>
        rule.NotEmpty().MaximumLength(256);

    public static IRuleBuilderOptions<T, string> AccountRole<T>(this IRuleBuilder<T, string> rule) =>
        rule.Must(role => Roles.All.Contains(role)).WithMessage(_ => string.Format(Messages.Accounts_UnknownRole, string.Join(", ", Roles.All)));

    // A team's Manager has to be an active Manager. A 400 on the field that names them, since
    // it's part of the request body, not the account the request is about.
    public static async Task RequireActiveManagerAsync(
        IUserAccounts accounts, Guid managerId, string propertyName, CancellationToken cancellationToken)
    {
        var manager = await accounts.FindAsync(managerId, cancellationToken);
        if (manager is not { Role: Roles.Manager, Status: AccountStatus.Active })
        {
            throw new ValidationException([new ValidationFailure(propertyName, Messages.Accounts_ChooseActiveManager)]);
        }
    }
}
