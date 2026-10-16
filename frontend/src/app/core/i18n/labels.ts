import { Role, Roles } from '../constants/roles';
import { AccountStatus } from '../models/account.model';
import { IssueCategory, IssueStatus } from '../models/issue.model';
import { AttendeeResponse, ReservationStatus } from '../models/reservation.model';
import { ResourceStatus } from '../models/resource.model';

// How the API's enum values read on screen (#58). The API sends them in English, as values;
// these are the words a person sees, in the app's language.

export function resourceStatusLabel(status: ResourceStatus): string {
  switch (status) {
    case 'Active':
      return $localize`:@@status.resource.active:Active`;
    case 'Inactive':
      return $localize`:@@status.resource.inactive:Inactive`;
    case 'Maintenance':
      return $localize`:@@status.resource.maintenance:Maintenance`;
  }
}

/**
 * A kind of resource, in the app's language. The kinds come from the database, seeded in
 * English (ResourceTypeSeedData), and stay English in the data: the default photos are picked by
 * those names. Only the seeded ones are known here; any other keeps its own name.
 */
export function resourceTypeLabel(name: string): string {
  switch (name) {
    case 'Meeting Room':
      return $localize`:@@resourceType.meetingRoom:Meeting Room`;
    case 'Desk':
      return $localize`:@@resourceType.desk:Desk`;
    case 'Company Car':
      return $localize`:@@resourceType.companyCar:Company Car`;
    case 'Projector':
      return $localize`:@@resourceType.projector:Projector`;
    case 'Camera':
      return $localize`:@@resourceType.camera:Camera`;
    default:
      return name;
  }
}

/** Kinds of resources in the order their names read in the app's language, for a list. */
export function byTypeLabel(a: { name: string }, b: { name: string }): number {
  return resourceTypeLabel(a.name).localeCompare(resourceTypeLabel(b.name), 'en');
}

export function accountStatusLabel(status: AccountStatus): string {
  switch (status) {
    case 'Active':
      return $localize`:@@status.account.active:Active`;
    case 'PendingApproval':
      return $localize`:@@status.account.pending:Waiting for approval`;
    case 'Disabled':
      return $localize`:@@status.account.disabled:Disabled`;
  }
}

export function reservationStatusLabel(status: ReservationStatus): string {
  switch (status) {
    case 'Confirmed':
      return $localize`:@@status.reservation.confirmed:Confirmed`;
    case 'Cancelled':
      return $localize`:@@status.reservation.cancelled:Cancelled`;
    case 'Pending':
      return $localize`:@@status.reservation.pending:Awaiting approval`;
  }
}

/** How someone invited answered, next to their name: "coming", "can't come", "no answer yet". */
export function attendeeResponseLabel(response: AttendeeResponse): string {
  switch (response) {
    case 'Accepted':
      return $localize`:@@attendee.accepted:coming`;
    case 'Declined':
      return $localize`:@@attendee.declined:can't come`;
    case 'Pending':
      return $localize`:@@attendee.pending:no answer yet`;
  }
}

export function roleLabel(role: Role): string {
  switch (role) {
    case Roles.Admin:
      return $localize`:@@role.admin:Admin`;
    case Roles.Manager:
      return $localize`:@@role.manager:Manager`;
    case Roles.Employee:
      return $localize`:@@role.employee:Employee`;
  }
}

/** What a reported problem is about (ADR-0037). */
export function issueCategoryLabel(category: IssueCategory): string {
  switch (category) {
    case 'Display':
      return $localize`:@@issue.category.display:Screen or TV`;
    case 'Projector':
      return $localize`:@@issue.category.projector:Projector`;
    case 'AudioVideo':
      return $localize`:@@issue.category.audioVideo:Camera or sound`;
    case 'AirConditioning':
      return $localize`:@@issue.category.airConditioning:Air conditioning`;
    case 'Cleaning':
      return $localize`:@@issue.category.cleaning:Cleaning`;
    case 'Furniture':
      return $localize`:@@issue.category.furniture:Furniture`;
    case 'Other':
      return $localize`:@@issue.category.other:Something else`;
  }
}

/** Where a reported problem stands. */
export function issueStatusLabel(status: IssueStatus): string {
  switch (status) {
    case 'Open':
      return $localize`:@@issue.status.open:Open`;
    case 'InProgress':
      return $localize`:@@issue.status.inProgress:In progress`;
    case 'Resolved':
      return $localize`:@@issue.status.resolved:Resolved`;
  }
}
