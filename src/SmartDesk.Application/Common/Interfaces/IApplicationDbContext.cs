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
    DbSet<ResourceBlock> ResourceBlocks { get; }
    DbSet<ResourceTeam> ResourceTeams { get; }
    DbSet<ResourcePhoto> ResourcePhotos { get; }
    DbSet<FloorPlan> FloorPlans { get; }
    DbSet<ReservationAttendee> ReservationAttendees { get; }
    DbSet<InboxMessage> InboxMessages { get; }
    DbSet<ReservationSeries> ReservationSeries { get; }
    DbSet<IssueReport> IssueReports { get; }
    DbSet<IssueReportPhoto> IssueReportPhotos { get; }
    DbSet<WaitlistEntry> WaitlistEntries { get; }
    DbSet<OfficeHours> OfficeHours { get; }
    DbSet<CalendarFeed> CalendarFeeds { get; }
    DbSet<FavoriteResource> FavoriteResources { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    // Forgets every change not saved yet, e.g. after a save the database refused, so the next
    // save starts clean (ADR-0043).
    void DiscardChanges();

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    // Must run inside a transaction. Locks the resource until that transaction ends, so
    // concurrent bookings of the same resource run one after another instead of racing
    // (ADR-0011). Bookings of different resources don't wait on each other.
    Task LockResourceForBookingAsync(Guid resourceId, CancellationToken cancellationToken = default);

    // Must run inside a transaction. Locks one reservation until that transaction ends, so an
    // update and a cancel of the same reservation can't interleave. A transaction takes at
    // most this lock and then one resource lock, always in that order, so locks can't cycle.
    Task LockReservationAsync(Guid reservationId, CancellationToken cancellationToken = default);

    // Must run inside a transaction. Locks one person's bookings until that transaction ends,
    // so two of theirs can't both pass a limit on how many they may have (ADR-0046). Taken
    // after the locks above, never before them.
    Task LockBookerAsync(Guid userId, CancellationToken cancellationToken = default);
}
