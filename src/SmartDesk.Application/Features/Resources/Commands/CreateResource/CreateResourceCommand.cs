using MediatR;
using SmartDesk.Application.Features.Resources.Common;

namespace SmartDesk.Application.Features.Resources.Commands.CreateResource;

// New resources always start Active; status changes go through UpdateResourceCommand.
public sealed record CreateResourceCommand(
    string Name,
    Guid ResourceTypeId,
    int Capacity,
    string Location,
    string? Description,
    IReadOnlyList<string>? Features) : IRequest<ResourceDto>, IResourceFields;
