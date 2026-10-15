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

    // A booking's page, which a calendar entry links back to (ADR-0048).
    string Reservation(Guid reservationId);

    // Someone's personal calendar feed (ADR-0048), an .ics address: the API's, reached through
    // the app's own address, which passes /api on. The token goes in the query string, which
    // the container's web server leaves out of its log; it's hex, so URL-safe.
    string CalendarFeed(string token);
}
