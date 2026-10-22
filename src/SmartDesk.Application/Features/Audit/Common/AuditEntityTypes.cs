namespace SmartDesk.Application.Features.Audit.Common;

// The names the history keeps rows under (ADR-0052), and the areas the history screen filters by.
public static class AuditEntityTypes
{
    public const string Reservation = "Reservation";
    public const string Attendee = "Attendee";
    public const string Resource = "Resource";
    public const string ResourceFeature = "ResourceFeature";
    public const string ResourceTeam = "ResourceTeam";
    public const string ResourcePhoto = "ResourcePhoto";
    public const string ResourceBlock = "ResourceBlock";
    public const string ResourceType = "ResourceType";
    public const string OfficeHours = "OfficeHours";
    public const string FloorPlan = "FloorPlan";
    public const string IssueReport = "IssueReport";
    public const string User = "User";
    public const string UserRole = "UserRole";
    public const string Passkey = "Passkey";

    public static readonly IReadOnlyDictionary<string, string[]> Areas = new Dictionary<string, string[]>
    {
        ["bookings"] = [Reservation, Attendee],
        ["resources"] = [Resource, ResourceFeature, ResourceTeam, ResourcePhoto, ResourceBlock, FloorPlan],
        ["rules"] = [ResourceType, OfficeHours],
        ["problems"] = [IssueReport],
        ["accounts"] = [User, UserRole, Passkey],
    };
}
