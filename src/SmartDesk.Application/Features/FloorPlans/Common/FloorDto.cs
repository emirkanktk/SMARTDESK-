namespace SmartDesk.Application.Features.FloorPlans.Common;

// A floor (ADR-0041): the location its resources share, how many of them the caller may use and
// how many of those are placed on its plan, and the plan's size when one is uploaded.
public sealed record FloorDto(string Location, int Resources, int Placed, FloorPlanImageDto? Plan);

// In pixels, so a client can lay the floor out before the image loads; UpdatedAt tells a new
// plan from a cached one.
public sealed record FloorPlanImageDto(int Width, int Height, DateTime UpdatedAt);
