using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Audit.Common;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Infrastructure.Persistence.Auditing;

// Writes the history of changes (ADR-0052) with the save that makes them: for every audited row
// it creates, changes or deletes, an AuditEntry in the same transaction, so the history can't
// miss a change that happened or keep one that was rolled back. Read from what the save
// changes, like FreedSlotsInterceptor, so every way of changing a row is in it, now and later -
// except writes that go straight to the database (ExecuteUpdate), which none of the audited
// changes do.
public sealed class AuditInterceptor(ICurrentUserService currentUser, TimeProvider timeProvider) : SaveChangesInterceptor
{
    // AuditEntries.Label's length.
    public const int LabelLength = 200;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Write(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Write(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Write(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        // A save that failed and is tried again would write its entries twice.
        foreach (var stale in context.ChangeTracker.Entries<AuditEntry>().Where(e => e.State == EntityState.Added).ToList())
        {
            stale.State = EntityState.Detached;
        }

        context.ChangeTracker.DetectChanges();
        var changed = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => (Entry: e, Type: AuditedTypes.Find(e.Metadata.ClrType)))
            .Where(e => e.Type is not null)
            .ToList();
        if (changed.Count == 0)
        {
            return;
        }

        var userId = currentUser.UserId;
        var actor = userId is not null ? AuditActor.User : currentUser.InRequest ? AuditActor.Anonymous : AuditActor.System;
        var at = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var (entry, type) in changed)
        {
            var changes = Changes(entry, type!);
            // Only columns the history leaves out, such as a reminder sent: nothing to tell.
            if (entry.State == EntityState.Modified && changes.Count == 0)
            {
                continue;
            }

            var label = type!.LabelOf(context, entry.Entity);
            context.Add(new AuditEntry
            {
                At = at,
                Actor = actor,
                UserId = userId,
                EntityType = type!.Name,
                EntityId = Key(entry),
                Action = entry.State switch
                {
                    EntityState.Added => AuditAction.Created,
                    EntityState.Deleted => AuditAction.Deleted,
                    _ => AuditAction.Updated,
                },
                ResourceId = type.ResourceOf(context, entry.Entity),
                // Never the reason a save fails: a long name is cut to the column.
                Label = label is { Length: > LabelLength } ? label[..LabelLength] : label,
                Changes = JsonSerializer.Serialize(changes, Json),
            });
        }
    }

    // A new row: what it has. A deleted one: what it had. A changed one: what changed.
    private static List<AuditChange> Changes(EntityEntry entry, AuditedType type)
    {
        var changes = new List<AuditChange>();
        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;
            if (!type.Audits(name))
            {
                continue;
            }

            var secret = type.IsSecret(name);
            switch (entry.State)
            {
                case EntityState.Added when Text(property.CurrentValue, secret) is { } value:
                    changes.Add(new AuditChange(name, null, value));
                    break;
                case EntityState.Deleted when Text(property.OriginalValue, secret) is { } value:
                    changes.Add(new AuditChange(name, value, null));
                    break;
                case EntityState.Modified when property.IsModified:
                    var before = Text(property.OriginalValue, secret);
                    var after = Text(property.CurrentValue, secret);
                    // A secret replaced by another is a change, though both read "***".
                    if (before != after || (secret && !Equals(property.OriginalValue, property.CurrentValue)))
                    {
                        changes.Add(new AuditChange(name, before, after));
                    }

                    break;
            }
        }

        return changes;
    }

    private static string Key(EntityEntry entry) =>
        string.Join('|', entry.Metadata.FindPrimaryKey()!.Properties.Select(p => Text(entry.Property(p.Name).CurrentValue, false)));

    // Every value as text, the same whatever the server's culture: instants in UTC.
    private static string? Text(object? value, bool secret) => value switch
    {
        null => null,
        _ when secret => "***",
        DateTime d => DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset o => o.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly t => t.ToString("HH:mm", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        Enum e => e.ToString(),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
