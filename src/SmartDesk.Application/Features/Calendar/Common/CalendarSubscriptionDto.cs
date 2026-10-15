namespace SmartDesk.Application.Features.Calendar.Common;

// The caller's personal calendar link (ADR-0048). Url is there only in the answer that made the
// link: just the token's hash is kept, so it can't be shown again.
public sealed record CalendarSubscriptionDto(string? Url, DateTime CreatedAt, DateTime? LastFetchedAt);
