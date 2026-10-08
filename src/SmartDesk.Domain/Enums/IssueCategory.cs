namespace SmartDesk.Domain.Enums;

// What a problem reported at a resource is about (ADR-0037). Two open reports of the same
// category on one resource are the same problem, except Other, which can be anything.
public enum IssueCategory
{
    // A screen or TV.
    Display = 0,

    Projector = 1,

    // A camera, microphone or speakers.
    AudioVideo = 2,

    AirConditioning = 3,

    Cleaning = 4,

    Furniture = 5,

    Other = 6,
}
