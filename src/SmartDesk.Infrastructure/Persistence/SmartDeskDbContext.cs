using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Domain.Entities;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Infrastructure.Persistence.Seed;

namespace SmartDesk.Infrastructure.Persistence;

public class SmartDeskDbContext(DbContextOptions<SmartDeskDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IApplicationDbContext
{
    public DbSet<ResourceType> ResourceTypes => Set<ResourceType>();
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<ResourceFeature> ResourceFeatures => Set<ResourceFeature>();
    public DbSet<Reservation> Reservations => Set<Reservation>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ResourceType>(entity =>
        {
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.HasData(ResourceTypeSeedData.All);
        });

        builder.Entity<Resource>(entity =>
        {
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Location).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(1000);

            entity.HasOne(e => e.ResourceType)
                .WithMany(rt => rt.Resources)
                .HasForeignKey(e => e.ResourceTypeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ResourceFeature>(entity =>
        {
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);

            entity.HasOne(e => e.Resource)
                .WithMany(r => r.Features)
                .HasForeignKey(e => e.ResourceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Reservation>(entity =>
        {
            entity.Property(e => e.Note).HasMaxLength(500);

            entity.HasOne(e => e.Resource)
                .WithMany(r => r.Reservations)
                .HasForeignKey(e => e.ResourceId)
                .OnDelete(DeleteBehavior.Cascade);

            // Speeds up the overlap-check query (same resource, active reservations) that
            // CreateReservationCommand will run on every write.
            entity.HasIndex(e => new { e.ResourceId, e.Status, e.StartDateTime, e.EndDateTime });

            // Business rule 2, enforced by the database as well as the validator.
            entity.ToTable(t => t.HasCheckConstraint(
                ReservationConstraints.EndAfterStart, "\"EndDateTime\" > \"StartDateTime\""));

            // Business rule 1's hard guarantee is a PostgreSQL exclusion constraint
            // (ReservationConstraints.NoOverlap). EF can't model it, so it's created in raw SQL
            // by the AddReservationOverlapConstraint migration - see ADR-0011.
        });
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        Database.BeginTransactionAsync(cancellationToken);

    public async Task LockResourceForBookingAsync(Guid resourceId, CancellationToken cancellationToken = default)
    {
        // Row lock on the resource, held until the caller's transaction commits or rolls back.
        // Without it, several simultaneous bookings of one slot all pass the overlap check and
        // then pile up on the exclusion constraint, where PostgreSQL resolves the tangle by
        // aborting some with deadlocks (40P01) - observed as 500s in a 30-way race test.
        // Queued behind this lock, each request instead sees the previous winner's committed
        // row and gets a clean 409. It also keeps an admin from deactivating the resource
        // between the status check and the insert.
        //
        // SQLite (unit tests) has no row locks and a single connection, so nothing to do there.
        if (Database.IsNpgsql())
        {
            await Database.ExecuteSqlAsync(
                $"SELECT 1 FROM \"Resources\" WHERE \"Id\" = {resourceId} FOR UPDATE", cancellationToken);
        }
    }

    public async Task LockReservationAsync(Guid reservationId, CancellationToken cancellationToken = default)
    {
        if (Database.IsNpgsql())
        {
            await Database.ExecuteSqlAsync(
                $"SELECT 1 FROM \"Reservations\" WHERE \"Id\" = {reservationId} FOR UPDATE", cancellationToken);
        }
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ExclusionViolation,
            ConstraintName: ReservationConstraints.NoOverlap,
        })
        {
            // Two requests for the same slot raced past the handler's own overlap check; the
            // database let exactly one of them in. Report it to the loser the same way.
            throw new ConflictException("The resource is already reserved for part of that time.", ex);
        }
    }
}
