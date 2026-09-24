using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Infrastructure.Persistence.Seed;

// Development-only sample resources (ADR-0007), so a fresh clone has something to search and
// book. Runs only while the Resources table is empty, so it never touches resources someone
// created or edited by hand.
public sealed class DemoResourceSeeder(SmartDeskDbContext dbContext, ILogger<DemoResourceSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await dbContext.Resources.AnyAsync(cancellationToken))
        {
            return;
        }

        var resources = new[]
        {
            Create("Meeting Room A", ResourceTypeSeedData.MeetingRoomId, 8, "2nd Floor", "TV", "Video Conference"),
            Create("Meeting Room B", ResourceTypeSeedData.MeetingRoomId, 4, "2nd Floor", "Whiteboard"),
            Create("Board Room", ResourceTypeSeedData.MeetingRoomId, 16, "5th Floor", "Projector", "Video Conference", "Whiteboard"),
            Create("Desk 3-01", ResourceTypeSeedData.DeskId, 1, "3rd Floor", "Monitor", "Docking Station"),
            Create("Desk 3-02", ResourceTypeSeedData.DeskId, 1, "3rd Floor", "Monitor"),
            Create("Company Car 34 SD 001", ResourceTypeSeedData.CompanyCarId, 5, "Parking B1"),
            Create("Projector P-1", ResourceTypeSeedData.ProjectorId, 1, "IT Storage", "HDMI"),
            // Not bookable - shows how a resource under maintenance looks in the UI.
            Create("Camera C-1", ResourceTypeSeedData.CameraId, 1, "IT Storage", ResourceStatus.Maintenance, "4K"),
        };

        dbContext.Resources.AddRange(resources);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Seeded {Count} demo resources", resources.Length);
    }

    private static Resource Create(string name, Guid typeId, int capacity, string location, params string[] features) =>
        Create(name, typeId, capacity, location, ResourceStatus.Active, features);

    private static Resource Create(
        string name, Guid typeId, int capacity, string location, ResourceStatus status, params string[] features) =>
        new()
        {
            Name = name,
            ResourceTypeId = typeId,
            Capacity = capacity,
            Location = location,
            Status = status,
            Features = features.Select(f => new ResourceFeature { Name = f }).ToList(),
        };
}
