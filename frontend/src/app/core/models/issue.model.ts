// Shapes of the problem report endpoints (ADR-0037) - see docs/API_CONTRACTS.md, Problem reports.

export const ISSUE_CATEGORIES = [
  'Display',
  'Projector',
  'AudioVideo',
  'AirConditioning',
  'Cleaning',
  'Furniture',
  'Other',
] as const;
export type IssueCategory = (typeof ISSUE_CATEGORIES)[number];

export const ISSUE_STATUSES = ['Open', 'InProgress', 'Resolved'] as const;
export type IssueStatus = (typeof ISSUE_STATUSES)[number];

/** A problem someone reported at a resource. */
export interface IssueReport {
  id: string;
  resourceId: string;
  resourceName: string;
  category: IssueCategory;
  description: string;
  /** The resource can't be used until it's fixed. */
  isBlocking: boolean;
  status: IssueStatus;
  /** Its photo comes from `GET /api/issues/{id}/photo`. */
  hasPhoto: boolean;
  reportedByUserId: string;
  /** Null when the account is gone. */
  reportedByName: string | null;
  createdAt: string;
  resolvedAt: string | null;
}

/** What `POST /api/resources/{id}/issues` takes, as a form. */
export interface NewIssueReport {
  category: IssueCategory;
  description: string;
  isBlocking: boolean;
  /** Already made smaller, like a resource's photo (ADR-0027). */
  photo: Blob | null;
}

export interface IssueFilter {
  status?: IssueStatus | null;
  resourceId?: string | null;
}

// Enforced by the API; the form mirrors it.
export const ISSUE_LIMITS = { descriptionMaxLength: 500 } as const;

/** While one of these is open on a resource, another of the same category is refused. */
export function canBeDuplicate(category: IssueCategory): boolean {
  return category !== 'Other';
}

/** The open report a new one of `category` would repeat, if any. */
export function sameProblem(
  open: readonly IssueReport[],
  category: IssueCategory,
): IssueReport | undefined {
  return canBeDuplicate(category)
    ? open.find((issue) => issue.category === category && issue.status !== 'Resolved')
    : undefined;
}
