using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.Resources.Commands.UpdateResource;

public sealed class UpdateResourceCommandHandler(
    IApplicationDbContext db, ICurrentUserService currentUser, ResourceReader reader, TimeProvider timeProvider)
    : IRequestHandler<UpdateResourceCommand, ResourceDto>
{
    public async Task<ResourceDto> Handle(UpdateResourceCommand request, CancellationToken cancellationToken)
    {
        // Under the booking lock, as for a status change: the status is part of the edit.
        await using (var transaction = await db.BeginTransactionAsync(cancellationToken))
        {
            await db.LockResourceForBookingAsync(request.Id, cancellationToken);

            var resource = await db.Resources
                .UsableBy(currentUser)
                .Include(r => r.Features)
                .Include(r => r.Teams)
                .SingleOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
                ?? throw new NotFoundException(nameof(Resource), request.Id);
            ResourceManagement.RequireManageable(resource, currentUser);
            await ResourceManagement.RequireNoUpcomingReservationsAsync(
                db, resource, request.Status, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

            resource.Name = request.Name.Trim();
            resource.ResourceTypeId = request.ResourceTypeId;
            resource.Capacity = request.Capacity;
            resource.Location = request.Location.Trim();
            resource.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            resource.Status = request.Status;
            resource.CheckInRequired = request.CheckInRequired;

            ReplaceFeatures(resource, ResourceFeatureNames.Normalize(request.Features));

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return (await reader.FindAsync(db.Resources.AsNoTracking().Where(r => r.Id == request.Id), cancellationToken))!;
    }

    // Diff rather than delete-all-and-reinsert: unchanged features keep their rows, only the
    // ones actually removed or added touch the database.
    private void ReplaceFeatures(Resource resource, IReadOnlyList<string> desired)
    {
        var removed = resource.Features
            .Where(f => !desired.Contains(f.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();
        db.ResourceFeatures.RemoveRange(removed);
        foreach (var feature in removed)
        {
            resource.Features.Remove(feature);
        }

        foreach (var name in desired)
        {
            var existing = resource.Features.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                resource.Features.Add(new ResourceFeature { Name = name });
            }
            else
            {
                existing.Name = name; // picks up a casing change, e.g. "tv" -> "TV"
            }
        }
    }
}
