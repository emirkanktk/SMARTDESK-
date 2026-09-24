using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Resources.Commands.CreateResource;

public sealed class CreateResourceCommandHandler(IApplicationDbContext db)
    : IRequestHandler<CreateResourceCommand, ResourceDto>
{
    public async Task<ResourceDto> Handle(CreateResourceCommand request, CancellationToken cancellationToken)
    {
        var resource = new Resource
        {
            Name = request.Name.Trim(),
            ResourceTypeId = request.ResourceTypeId,
            Capacity = request.Capacity,
            Location = request.Location.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Status = ResourceStatus.Active,
            Features = ResourceFeatureNames.Normalize(request.Features)
                .Select(name => new ResourceFeature { Name = name })
                .ToList(),
        };

        db.Resources.Add(resource);
        await db.SaveChangesAsync(cancellationToken);

        return await db.Resources
            .AsNoTracking()
            .Where(r => r.Id == resource.Id)
            .Select(ResourceDto.Projection)
            .SingleAsync(cancellationToken);
    }
}
