namespace SmartDesk.Application.Common.Interfaces;

// Absolute links into the Angular app, for emails. The base address is configuration
// (App:FrontendUrl), since the API can't know where the frontend is served.
public interface IFrontendLinks
{
    // `token` must already be URL-safe (IIdentityService hands it out that way).
    string ResetPassword(string email, string token);

    string SignIn();

    string ForgotPassword();

    // The admin's user list, where sign-ups wait for approval.
    string Users();

    // A Manager's team, where requests to join it wait for approval.
    string Team();
}
