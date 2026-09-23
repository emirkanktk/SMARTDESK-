using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
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
        });
    }
}
