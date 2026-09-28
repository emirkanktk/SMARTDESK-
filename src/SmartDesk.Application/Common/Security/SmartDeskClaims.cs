namespace SmartDesk.Application.Common.Security;

// Claim types SmartDesk itself puts on a request's user (ADR-0021).
public static class SmartDeskClaims
{
    // In the token: a digest of the account's security stamp when the token was issued. Any
    // password change moves the stamp, so every earlier token stops working (sessions end).
    public const string SessionStamp = "sst";

    // Added while the token is validated, from the account as it is now - never trusted from
    // the token, so moving someone to another team takes effect on their next request.
    public const string Team = "smartdesk:team";

    // Added while the token is validated when the account must choose a new password first.
    public const string PasswordChangeRequired = "smartdesk:password_change_required";
}
