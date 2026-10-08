import { IssueReport } from '../app/core/models/issue.model';

/** Zeynep's report of a TV that doesn't turn on in Meeting Room A, at 09:30 local time, open. */
export function fakeIssue(overrides: Partial<IssueReport> = {}): IssueReport {
  return {
    id: 'iss-1',
    resourceId: 'res-1',
    resourceName: 'Meeting Room A',
    category: 'Display',
    description: "The TV doesn't turn on",
    isBlocking: false,
    status: 'Open',
    hasPhoto: false,
    reportedByUserId: 'u-zeynep',
    reportedByName: 'Zeynep Kaya',
    createdAt: new Date('2026-10-07T09:30').toISOString(),
    resolvedAt: null,
    ...overrides,
  };
}
