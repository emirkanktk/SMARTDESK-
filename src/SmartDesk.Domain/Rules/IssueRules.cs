using System.Linq.Expressions;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Domain.Rules;

// Reported problems (ADR-0037). One problem is reported once: while a report of a kind is
// still open on a resource, another of the same kind is the same problem. Other is the
// exception, since two reports of it are rarely about the same thing.
public static class IssueRules
{
    // Room enough to say what's wrong, and what an inbox message's note holds.
    public const int MaxDescriptionLength = 500;

    // Not fixed yet: Open, or InProgress.
    public static Expression<Func<IssueReport, bool>> Unresolved => i => i.Status != IssueStatus.Resolved;

    public static bool CanBeDuplicate(IssueCategory category) => category != IssueCategory.Other;

    // An unresolved report of `category` on the resource, which a new one would repeat.
    public static Expression<Func<IssueReport, bool>> SameProblem(Guid resourceId, IssueCategory category) =>
        i => i.ResourceId == resourceId && i.Category == category && i.Status != IssueStatus.Resolved;
}
