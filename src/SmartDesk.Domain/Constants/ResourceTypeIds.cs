namespace SmartDesk.Domain.Constants;

// The kinds of resource every office has, with the same ids everywhere: the migration seeds them,
// and there's no endpoint to add others. What treats a kind in a way of its own goes by these,
// as a lobby screen does its rooms and desks (ADR-0064).
public static class ResourceTypeIds
{
    public static readonly Guid MeetingRoom = new("3f2b6a1e-8c4d-4e0f-9a51-2d7b6c1e0a01");
    public static readonly Guid Desk = new("3f2b6a1e-8c4d-4e0f-9a51-2d7b6c1e0a02");
    public static readonly Guid CompanyCar = new("3f2b6a1e-8c4d-4e0f-9a51-2d7b6c1e0a03");
    public static readonly Guid Projector = new("3f2b6a1e-8c4d-4e0f-9a51-2d7b6c1e0a04");
    public static readonly Guid Camera = new("3f2b6a1e-8c4d-4e0f-9a51-2d7b6c1e0a05");
}
