/**
 * The signed-in user's personal calendar link (ADR-0048). `url` is only in the answer that made
 * the link: just its token's hash is kept, so it can't be shown again.
 */
export interface CalendarSubscription {
  url: string | null;
  createdAt: string;
  /** When a calendar app last fetched it; null until one has. */
  lastFetchedAt: string | null;
}
