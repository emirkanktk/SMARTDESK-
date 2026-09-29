import { Resource } from '../../core/models/resource.model';

/**
 * The API's management rule (ADR-0022), mirrored so screens only offer what will work: an admin
 * manages every resource; a manager only one that belongs to their team alone. A shared
 * resource, or one other teams use too, stays an admin's. The API checks again.
 */
export function canManageResource(
  resource: Resource,
  user: { id: string; isAdmin: boolean; isManager: boolean },
): boolean {
  return (
    user.isAdmin ||
    (user.isManager && resource.teams.length === 1 && resource.teams[0].id === user.id)
  );
}

/** "Shared", "Zeynep Kaya's team", or several managers' names. */
export function teamLabel(resource: Resource): string {
  if (resource.teams.length === 0) return 'Shared';
  if (resource.teams.length === 1) return `${resource.teams[0].managerName}'s team`;
  return resource.teams.map((t) => t.managerName).join(', ');
}
