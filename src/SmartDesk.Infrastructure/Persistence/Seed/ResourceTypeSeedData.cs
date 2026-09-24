using SmartDesk.Domain.Entities;

namespace SmartDesk.Infrastructure.Persistence.Seed;

// Reference data, seeded through the migration (HasData) so every environment has the same
// types with the same ids. There's no endpoint to create types yet; adding one means a new
// migration.
public static class ResourceTypeSeedData
{
    public static readonly Guid MeetingRoomId = new("3f2b6a1e-8c4d-4e0f-9a51-2d7b6c1e0a01");
    public static readonly Guid DeskId = new("3f2b6a1e-8c4d-4e0f-9a51-2d7b6c1e0a02");
    public static readonly Guid CompanyCarId = new("3f2b6a1e-8c4d-4e0f-9a51-2d7b6c1e0a03");
    public static readonly Guid ProjectorId = new("3f2b6a1e-8c4d-4e0f-9a51-2d7b6c1e0a04");
    public static readonly Guid CameraId = new("3f2b6a1e-8c4d-4e0f-9a51-2d7b6c1e0a05");

    public static IReadOnlyList<ResourceType> All =>
    [
        new() { Id = MeetingRoomId, Name = "Meeting Room" },
        new() { Id = DeskId, Name = "Desk" },
        new() { Id = CompanyCarId, Name = "Company Car" },
        new() { Id = ProjectorId, Name = "Projector" },
        new() { Id = CameraId, Name = "Camera" },
    ];
}
