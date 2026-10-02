namespace SmartDesk.Application.Common.Interfaces;

// Absolute links into the Angular app, for emails and QR codes. The base address is
// configuration (App:FrontendUrl), since the API can't know where the frontend is served.
public interface IFrontendLinks
{
    // The check-in page a resource's QR code opens (ADR-0030). `code` is hex, so URL-safe.
    string CheckIn(Guid resourceId, string code);

    // The page a resource's door screen opens (ADR-0034). The token goes after '#', so it never
    // reaches a server or its logs; it's hex, so URL-safe.
    string DoorScreen(Guid resourceId, string token);

    // `token` must already be URL-safe (IIdentityService hands it out that way).
    string ResetPassword(string email, string token);

    string SignIn();

    string ForgotPassword();

    // The admin's user list, where sign-ups wait for approval.
    string Users();

    // A Manager's team, where requests to join it wait for approval.
    string Team();
}
