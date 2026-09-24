using Microsoft.EntityFrameworkCore;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Common.Interfaces;

// What handlers see of the database. Implemented by SmartDeskDbContext in Infrastructure;
// Application depends on EF Core's abstractions (DbSet, LINQ) but never on Npgsql - see
// docs/DECISIONS.md, ADR-0009.
public interface IApplicationDbContext
{
    DbSet<ResourceType> ResourceTypes { get; }
    DbSet<Resource> Resources { get; }
    DbSet<ResourceFeature> ResourceFeatures { get; }
    DbSet<Reservation> Reservations { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
