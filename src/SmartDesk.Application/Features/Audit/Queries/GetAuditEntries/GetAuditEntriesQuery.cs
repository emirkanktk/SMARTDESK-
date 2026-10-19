using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Audit.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Audit.Queries.GetAuditEntries;

// The history of changes (GET /api/audit, Admin, ADR-0052), newest first, a page at a time:
// everything, an area of it, one resource's, one row's, one person's, or a time range's. Before
// is the Id of the last entry already shown. The people and resources the entries mention come
// named alongside, as they're called now.
public sealed record GetAuditEntriesQuery(
    string? Area = null,
    Guid? ResourceId = null,
    string? EntityType = null,
    string? EntityId = null,
    Guid? UserId = null,
    DateTime? From = null,
    DateTime? To = null,
    long? Before = null,
    int Take = GetAuditEntriesQueryHandler.DefaultTake) : IRequest<AuditPageDto>;

public sealed record AuditPageDto(
    IReadOnlyList<AuditEntryDto> Items,
    bool HasMore,
    IReadOnlyDictionary<Guid, string> People,
    IReadOnlyDictionary<Guid, string> Resources);

public sealed record AuditEntryDto(
    long Id,
    DateTime At,
    AuditActor Actor,
    Guid? UserId,
    string EntityType,
    string EntityId,
    AuditAction Action,
    Guid? ResourceId,
    string? Label,
    IReadOnlyList<AuditChange> Changes);

public sealed class GetAuditEntriesQueryValidator : AbstractValidator<GetAuditEntriesQuery>
{
    public GetAuditEntriesQueryValidator()
    {
        RuleFor(x => x.Area)
            .Must(area => AuditEntityTypes.Areas.ContainsKey(area!))
            .When(x => x.Area is not null)
            .WithMessage(_ => Messages.Audit_UnknownArea);
        RuleFor(x => x.EntityType).NotEmpty().When(x => x.EntityId is not null);
        RuleFor(x => x.To).GreaterThan(x => x.From).When(x => x.From is not null && x.To is not null);
        RuleFor(x => x.Take).InclusiveBetween(1, GetAuditEntriesQueryHandler.MaxTake);
    }
}

public sealed class GetAuditEntriesQueryHandler(IApplicationDbContext db, IIdentityService identityService)
    : IRequestHandler<GetAuditEntriesQuery, AuditPageDto>
{
    public const int DefaultTake = 50;
    public const int MaxTake = 200;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Columns whose values are people: whose booking or account, who checked in or decided, a
    // manager, or a team, whose id is its Manager's (ADR-0022).
    private static readonly HashSet<string> PersonColumns =
        ["UserId", "CheckedInByUserId", "DecidedByUserId", "ManagerId", "TeamId"];

    public async Task<AuditPageDto> Handle(GetAuditEntriesQuery request, CancellationToken cancellationToken)
    {
        var entries = db.AuditEntries.AsNoTracking();
        if (request.Area is { } area)
        {
            var types = AuditEntityTypes.Areas[area];
            entries = entries.Where(e => types.Contains(e.EntityType));
        }

        if (request.ResourceId is { } resourceId)
        {
            entries = entries.Where(e => e.ResourceId == resourceId);
        }

        if (request.EntityType is { } entityType)
        {
            entries = entries.Where(e => e.EntityType == entityType);
        }

        if (request.EntityId is { } entityId)
        {
            entries = entries.Where(e => e.EntityId == entityId);
        }

        if (request.UserId is { } userId)
        {
            entries = entries.Where(e => e.UserId == userId);
        }

        if (request.From is { } from)
        {
            entries = entries.Where(e => e.At >= from);
        }

        if (request.To is { } to)
        {
            entries = entries.Where(e => e.At < to);
        }

        if (request.Before is { } before)
        {
            entries = entries.Where(e => e.Id < before);
        }

        // One more than asked, to know whether there's a next page.
        var rows = await entries.OrderByDescending(e => e.Id).Take(request.Take + 1).ToListAsync(cancellationToken);
        var items = rows
            .Take(request.Take)
            .Select(e => new AuditEntryDto(
                e.Id,
                e.At,
                e.Actor,
                e.UserId,
                e.EntityType,
                e.EntityId,
                e.Action,
                e.ResourceId,
                e.Label,
                JsonSerializer.Deserialize<List<AuditChange>>(e.Changes, Json) ?? []))
            .ToList();

        var people = new HashSet<Guid>();
        var resources = new HashSet<Guid>();
        foreach (var item in items)
        {
            AddId(people, item.UserId?.ToString());
            if (item.EntityType == AuditEntityTypes.User)
            {
                AddId(people, item.EntityId);
            }

            AddId(resources, item.ResourceId?.ToString());
            foreach (var change in item.Changes)
            {
                var into = PersonColumns.Contains(change.Property) ? people : change.Property == "ResourceId" ? resources : null;
                if (into is not null)
                {
                    AddId(into, change.Old);
                    AddId(into, change.New);
                }
            }
        }

        var names = await identityService.GetDisplayNamesAsync(people.ToList(), cancellationToken);
        var resourceNames = resources.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Resources
                .AsNoTracking()
                .Where(r => resources.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);
        return new AuditPageDto(items, rows.Count > request.Take, names, resourceNames);
    }

    private static void AddId(HashSet<Guid> ids, string? value)
    {
        if (Guid.TryParse(value, out var id))
        {
            ids.Add(id);
        }
    }
}
