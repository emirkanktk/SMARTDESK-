using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Infrastructure.Persistence.Seed;

// Reference data, seeded through the migration (HasData) so every environment has the same
// types with the same ids. There's no endpoint to create types yet; adding one means a new
// migration.
public static class ResourceTypeSeedData
{
    public static readonly Guid MeetingRoomId = ResourceTypeIds.MeetingRoom;
    public static readonly Guid DeskId = ResourceTypeIds.Desk;
    public static readonly Guid CompanyCarId = ResourceTypeIds.CompanyCar;
    public static readonly Guid ProjectorId = ResourceTypeIds.Projector;
    public static readonly Guid CameraId = ResourceTypeIds.Camera;

    public static IReadOnlyList<ResourceType> All =>
    [
        new() { Id = MeetingRoomId, Name = "Meeting Room" },
        new() { Id = DeskId, Name = "Desk" },
        new() { Id = CompanyCarId, Name = "Company Car" },
        new() { Id = ProjectorId, Name = "Projector" },
        new() { Id = CameraId, Name = "Camera" },
    ];
}
