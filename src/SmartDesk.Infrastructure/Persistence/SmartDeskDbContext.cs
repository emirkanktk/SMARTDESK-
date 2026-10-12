using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Rules;
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
    public DbSet<ResourceBlock> ResourceBlocks => Set<ResourceBlock>();
    public DbSet<ResourceTeam> ResourceTeams => Set<ResourceTeam>();
    public DbSet<ResourcePhoto> ResourcePhotos => Set<ResourcePhoto>();
    public DbSet<FloorPlan> FloorPlans => Set<FloorPlan>();
    public DbSet<ReservationAttendee> ReservationAttendees => Set<ReservationAttendee>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<ReservationSeries> ReservationSeries => Set<ReservationSeries>();
    public DbSet<IssueReport> IssueReports => Set<IssueReport>();
    public DbSet<IssueReportPhoto> IssueReportPhotos => Set<IssueReportPhoto>();

    public DbSet<UserSession> UserSessions => Set<UserSession>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            // Existing accounts get "now" as their creation time (and Status 0, Active).
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.Language).HasMaxLength(8);

            // A team is the users pointing at the same Manager. Restrict: a Manager with members
            // can't be deleted out from under them (only unapproved sign-ups are ever deleted).
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(e => e.ManagerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

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
            entity.Property(e => e.CheckInCode).IsRequired().HasMaxLength(CheckInRules.CodeLength);
            // SHA-256 of a door screen's token, as 64 hex digits (ADR-0034).
            entity.Property(e => e.DoorScreenTokenHash).HasMaxLength(64);

            entity.HasOne(e => e.ResourceType)
                .WithMany(rt => rt.Resources)
                .HasForeignKey(e => e.ResourceTypeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Sign-in sessions (ADR-0026): looked up by the hash of the refresh token presented,
        // which may be the current one or, for a few seconds, the one before.
        builder.Entity<UserSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TokenHash).IsRequired().HasMaxLength(64);
            entity.Property(e => e.PreviousTokenHash).HasMaxLength(64);
            entity.Property(e => e.SessionStamp).IsRequired().HasMaxLength(32);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => e.PreviousTokenHash);
            entity.HasIndex(e => e.UserId);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // A resource's photo: one row keyed by the resource, deleted with it.
        builder.Entity<ResourcePhoto>(entity =>
        {
            entity.HasKey(e => e.ResourceId);
            entity.Property(e => e.ContentType).IsRequired().HasMaxLength(32);
            entity.Property(e => e.Data).IsRequired();
            entity.HasOne<Resource>()
                .WithOne(r => r.Photo)
                .HasForeignKey<ResourcePhoto>(e => e.ResourceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // A floor's drawing (ADR-0041): one row per location, as long as a resource's Location
        // may be. Not tied to the resources by a key: a plan may come before them.
        builder.Entity<FloorPlan>(entity =>
        {
            entity.HasKey(e => e.Location);
            entity.Property(e => e.Location).HasMaxLength(200);
            entity.Property(e => e.ContentType).IsRequired().HasMaxLength(32);
            entity.Property(e => e.Data).IsRequired();
        });

        // ADR-0022. The team id is a Manager's account id, with no foreign key across the
        // Identity boundary; indexed for "which resources does this team have".
        builder.Entity<ResourceTeam>(entity =>
        {
            entity.HasKey(e => new { e.ResourceId, e.TeamId });
            entity.HasIndex(e => e.TeamId);
            entity.HasOne(e => e.Resource)
                .WithMany(r => r.Teams)
                .HasForeignKey(e => e.ResourceId)
                .OnDelete(DeleteBehavior.Cascade);
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

            // The reminder job (ADR-0018) looks every 30 seconds for reminders still to send
            // among reservations starting soon; this keeps that to a scan of just those rows.
            entity.HasIndex(e => e.StartDateTime)
                .HasDatabaseName("IX_Reservations_PendingReminders")
                .HasFilter("\"ReminderMinutes\" IS NOT NULL AND \"ReminderSentAt\" IS NULL");

            // Likewise for the release job (ADR-0030): confirmed bookings that need a check-in
            // nobody has made yet. A booking leaves it when checked in, released or cancelled.
            // Named, because an unnamed HasIndex on the same column would be the index above.
            entity.HasIndex(e => e.StartDateTime, "IX_Reservations_AwaitingCheckIn")
                .HasFilter("\"CheckInRequired\" AND \"CheckedInAt\" IS NULL AND \"Status\" = 0");

            // Business rule 2, enforced by the database as well as the validator.
            entity.ToTable(t => t.HasCheckConstraint(
                ReservationConstraints.EndAfterStart, "\"EndDateTime\" > \"StartDateTime\""));

            // Business rule 1's hard guarantee is a PostgreSQL exclusion constraint
            // (ReservationConstraints.NoOverlap). EF can't model it, so it's created in raw SQL
            // by the AddReservationOverlapConstraint migration - see ADR-0011.

            // The dates of a series (ADR-0035), e.g. to cancel the rest of it. Bookings are
            // cancelled rather than deleted, and so are series, so nothing cascades.
            entity.HasOne(e => e.Series)
                .WithMany()
                .HasForeignKey(e => e.SeriesId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.SeriesId);
        });

        builder.Entity<ReservationSeries>(entity =>
        {
            entity.Property(e => e.TimeZone).IsRequired().HasMaxLength(100);
            entity.ToTable(t => t.HasCheckConstraint(
                "CK_ReservationSeries_Interval", $"\"Interval\" BETWEEN 1 AND {RecurrenceRules.MaxInterval}"));
        });

        builder.Entity<ResourceBlock>(entity =>
        {
            entity.Property(e => e.Reason).IsRequired().HasMaxLength(200);

            entity.HasOne(e => e.Resource)
                .WithMany(r => r.Blocks)
                .HasForeignKey(e => e.ResourceId)
                .OnDelete(DeleteBehavior.Cascade);

            // Serves both "is this resource blocked for [start, end)?" on every booking and the
            // calendar's range query.
            entity.HasIndex(e => new { e.ResourceId, e.StartDateTime, e.EndDateTime });

            entity.ToTable(t => t.HasCheckConstraint(
                "CK_ResourceBlocks_EndAfterStart", "\"EndDateTime\" > \"StartDateTime\""));
        });

        // ADR-0032. The attendee is an account id, with no foreign key across the Identity
        // boundary; indexed for "the meetings I'm invited to".
        builder.Entity<ReservationAttendee>(entity =>
        {
            entity.HasKey(e => new { e.ReservationId, e.UserId });
            entity.HasIndex(e => e.UserId);
            entity.Property(e => e.DeclineReason).HasMaxLength(500);
            entity.HasOne(e => e.Reservation)
                .WithMany(r => r.Attendees)
                .HasForeignKey(e => e.ReservationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // A message keeps its own copy of the meeting, so it outlives the booking it links to.
        builder.Entity<InboxMessage>(entity =>
        {
            entity.Property(e => e.ResourceName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Note).HasMaxLength(500);
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.HasOne<Reservation>()
                .WithMany()
                .HasForeignKey(e => e.ReservationId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Series)
                .WithMany()
                .HasForeignKey(e => e.SeriesId)
                .OnDelete(DeleteBehavior.SetNull);

            // Someone's inbox, newest first; and how many they haven't read, which the app asks
            // for on every page.
            entity.HasIndex(e => new { e.RecipientId, e.CreatedAt });
            entity.HasIndex(e => e.RecipientId, "IX_InboxMessages_Unread")
                .HasFilter("\"ReadAt\" IS NULL");
        });

        // ADR-0037. The reporter is an account id, with no foreign key across the Identity
        // boundary. Reports go with their resource, like its bookings.
        builder.Entity<IssueReport>(entity =>
        {
            entity.Property(e => e.Description).IsRequired().HasMaxLength(IssueRules.MaxDescriptionLength);
            entity.HasOne(e => e.Resource)
                .WithMany()
                .HasForeignKey(e => e.ResourceId)
                .OnDelete(DeleteBehavior.Cascade);

            // "Is this problem already reported here?" on every report, and the problems still
            // open on a resource, which the page its QR code opens asks for.
            entity.HasIndex(e => new { e.ResourceId, e.Status, e.Category });
            // The list for those who look after resources: newest first.
            entity.HasIndex(e => e.CreatedAt);
        });

        // A report's photo: one row keyed by the report, deleted with it.
        builder.Entity<IssueReportPhoto>(entity =>
        {
            entity.HasKey(e => e.IssueReportId);
            entity.Property(e => e.ContentType).IsRequired().HasMaxLength(32);
            entity.Property(e => e.Data).IsRequired();
            entity.HasOne<IssueReport>()
                .WithOne(i => i.Photo)
                .HasForeignKey<IssueReportPhoto>(e => e.IssueReportId)
                .OnDelete(DeleteBehavior.Cascade);
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
            throw new ConflictException(Messages.Booking_TakenResource, ex)
            {
                Code = ConflictCodes.ReservationOverlaps,
            };
        }
    }
}
