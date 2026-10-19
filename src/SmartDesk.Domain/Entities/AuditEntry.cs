using SmartDesk.Domain.Enums;

namespace SmartDesk.Domain.Entities;

// One row a save created, changed or deleted, written by that same save (ADR-0052): who did it,
// when, and what it was before and after. Never changed once written.
public class AuditEntry
{
    // In the order the entries were written, which is how they're listed and paged.
    public long Id { get; set; }

    public DateTime At { get; set; }

    public AuditActor Actor { get; set; }

    // The signed-in person, when Actor is User; an account id, with no foreign key across the
    // Identity boundary.
    public Guid? UserId { get; set; }

    // "Reservation", "Resource", "User"...: the kind of row, by a name of its own that a rename
    // of the class doesn't change.
    public string EntityType { get; set; } = string.Empty;

    // The row's key as text; "a|b" for a key of two parts.
    public string EntityId { get; set; } = string.Empty;

    public AuditAction Action { get; set; }

    // The resource it's about, when there is one, so a resource's history is one query.
    public Guid? ResourceId { get; set; }

    // A few words that say which row it was, as it was then: a resource's name, a booking's
    // times. Kept as data, not in anyone's language.
    public string? Label { get; set; }

    // JSON: [{ "property": "Status", "old": "Confirmed", "new": "Cancelled" }], every value as
    // text. Secrets show only whether there is one ("***").
    public string Changes { get; set; } = "[]";
}
