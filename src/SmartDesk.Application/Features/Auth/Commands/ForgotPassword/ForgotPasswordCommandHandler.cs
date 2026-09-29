using MediatR;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Localization;
using SmartDesk.Application.Features.Auth.Common;

namespace SmartDesk.Application.Features.Auth.Commands.ForgotPassword;

public sealed class ForgotPasswordCommandHandler(
    IIdentityService identityService,
    IEmailSender emailSender,
    IFrontendLinks links) : IRequestHandler<ForgotPasswordCommand>
{
    public async Task Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var reset = await identityService.CreatePasswordResetTokenAsync(request.Email.Trim(), cancellationToken);

        // Nothing to send for an unknown email - and nothing different to answer either. The
        // email is only queued, so sending it doesn't make this path slower.
        if (reset is not null)
        {
            emailSender.Queue(AccountEmails.PasswordReset(Languages.CultureFor(reset.User.Language), reset.User, links.ResetPassword(reset.User.Email, reset.Token)));
        }
    }
}
