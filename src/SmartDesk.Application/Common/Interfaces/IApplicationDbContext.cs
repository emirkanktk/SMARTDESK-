using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    // Must run inside a transaction. Locks the resource until that transaction ends, so
    // concurrent bookings of the same resource run one after another instead of racing
    // (ADR-0011). Bookings of different resources don't wait on each other.
    Task LockResourceForBookingAsync(Guid resourceId, CancellationToken cancellationToken = default);
}
