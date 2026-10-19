import { formatDate } from '@angular/common';
import {
  accountStatusLabel,
  attendeeResponseLabel,
  issueCategoryLabel,
  issueStatusLabel,
  reservationStatusLabel,
  resourceStatusLabel,
  resourceTypeLabel,
  roleLabel,
} from '../../core/i18n/labels';
import { AccountStatus } from '../../core/models/account.model';
import { AuditAction, AuditChange, AuditEntry, AuditPage } from '../../core/models/audit.model';
import { Role } from '../../core/constants/roles';
import { IssueCategory, IssueStatus } from '../../core/models/issue.model';
import { AttendeeResponse, ReservationStatus } from '../../core/models/reservation.model';
import { ResourceStatus } from '../../core/models/resource.model';

/** What the history screen needs besides an entry to say it in words (ADR-0052). */
export interface AuditContext {
  people: Record<string, string>;
  resources: Record<string, string>;
  /** Kinds of resource by id, for a resource that changed kind. */
  types: Record<string, string>;
  locale: string;
}

/** One line of what changed: the column, and what it went from and to, in words. */
export interface ChangeLine {
  label: string;
  text: string;
}

/** The kind of row an entry is about, as the screens call it. */
export function entityLabel(entityType: string): string {
  switch (entityType) {
    case 'Reservation':
      return $localize`:@@history.entity.reservation:Booking`;
    case 'Attendee':
      return $localize`:@@history.entity.attendee:Invitation`;
    case 'Resource':
      return $localize`:@@history.entity.resource:Resource`;
    case 'ResourceFeature':
      return $localize`:@@history.entity.feature:Feature`;
    case 'ResourceTeam':
      return $localize`:@@history.entity.team:Team`;
    case 'ResourcePhoto':
      return $localize`:@@history.entity.photo:Photo`;
    case 'ResourceBlock':
      return $localize`:@@history.entity.block:Blocked window`;
    case 'ResourceType':
      return $localize`:@@history.entity.type:Booking rules of a kind`;
    case 'OfficeHours':
      return $localize`:@@history.entity.officeHours:Office hours`;
    case 'FloorPlan':
      return $localize`:@@history.entity.floorPlan:Floor plan`;
    case 'IssueReport':
      return $localize`:@@history.entity.issue:Problem`;
    case 'User':
      return $localize`:@@history.entity.user:Account`;
    case 'UserRole':
      return $localize`:@@history.entity.role:Role`;
    default:
      return entityType;
  }
}

export function actionLabel(action: AuditAction): string {
  switch (action) {
    case 'Created':
      return $localize`:@@history.action.created:Added`;
    case 'Updated':
      return $localize`:@@history.action.updated:Changed`;
    case 'Deleted':
      return $localize`:@@history.action.deleted:Removed`;
  }
}

/** Who a change came from: a person, someone without an account at the door, or SmartDesk. */
export function actorLabel(entry: AuditEntry, people: Record<string, string>): string {
  switch (entry.actor) {
    case 'User':
      return (entry.userId && people[entry.userId]) || $localize`:@@common.someone:Someone`;
    case 'Anonymous':
      return $localize`:@@history.actor.anonymous:Someone without an account`;
    case 'System':
      return $localize`:@@history.actor.system:SmartDesk, by itself`;
  }
}

/** Which row it was: "Board Room · Mon 19 Oct, 11:00-12:00", "Ayşe Yılmaz", "Office hours". */
export function subject(entry: AuditEntry, context: AuditContext): string {
  const resource = entry.resourceId ? context.resources[entry.resourceId] : undefined;
  const parts: (string | null | undefined)[] = [];
  switch (entry.entityType) {
    case 'Reservation':
    case 'ResourceBlock':
    case 'Attendee':
      parts.push(resource, entry.label ? times(entry.label, context.locale) : null);
      break;
    case 'Resource':
      parts.push(resource ?? entry.label);
      break;
    case 'ResourceFeature':
      parts.push(resource, entry.label);
      break;
    case 'ResourceTeam':
    case 'ResourcePhoto':
      parts.push(resource);
      break;
    case 'ResourceType':
      parts.push(entry.label ? resourceTypeLabel(entry.label) : null);
      break;
    case 'IssueReport':
      parts.push(resource, entry.label ? issueCategoryLabel(entry.label as IssueCategory) : null);
      break;
    case 'User':
      parts.push(context.people[entry.entityId] ?? entry.label);
      break;
    case 'UserRole': {
      const person = entry.changes.find((c) => c.property === 'UserId');
      const id = person?.new ?? person?.old;
      parts.push(
        entry.label ? roleLabel(entry.label as Role) : null,
        id ? context.people[id] : null,
      );
      break;
    }
    default:
      parts.push(entry.label);
  }
  return parts.filter((p): p is string => !!p).join(' · ');
}

/** What changed, in words, leaving out what the subject already says or nobody needs to read. */
export function changeLines(entry: AuditEntry, context: AuditContext): ChangeLine[] {
  return entry.changes
    .filter((change) => shown(entry, change))
    .map((change) => {
      const label = propertyLabel(change.property);
      const secret = secretText(change);
      if (secret) return { label, text: secret };
      const before = value(entry, change.property, change.old, context);
      const after = value(entry, change.property, change.new, context);
      const text =
        change.old === null ? after : change.new === null ? before : `${before} → ${after}`;
      return { label, text };
    });
}

// Technical columns, and ids the subject or the entry itself already says.
const HIDDEN: Record<string, string[]> = {
  ResourcePhoto: ['ContentType', 'UpdatedAt'],
  FloorPlan: ['ContentType', 'Width', 'Height', 'UpdatedAt'],
  ResourceBlock: ['CreatedByUserId'],
  Attendee: ['ReservationId'],
};

function shown(entry: AuditEntry, change: AuditChange): boolean {
  if (HIDDEN[entry.entityType]?.includes(change.property)) return false;
  // The resource is in the subject; only a move to another one is worth a line.
  if (change.property === 'ResourceId') return entry.action === 'Updated';
  // A role's person is in its subject too.
  return !(entry.entityType === 'UserRole' && change.property === 'UserId');
}

function propertyLabel(property: string): string {
  switch (property) {
    case 'StartDateTime':
      return $localize`:@@history.field.start:Start`;
    case 'EndDateTime':
      return $localize`:@@history.field.end:End`;
    case 'Status':
      return $localize`:@@history.field.status:Status`;
    case 'Note':
      return $localize`:@@history.field.note:Note`;
    case 'ReminderMinutes':
      return $localize`:@@history.field.reminder:Reminder, minutes before`;
    case 'CheckInRequired':
      return $localize`:@@history.field.checkInRequired:Needs a check-in`;
    case 'CheckedInAt':
      return $localize`:@@history.field.checkedInAt:Checked in`;
    case 'CheckedInByUserId':
      return $localize`:@@history.field.checkedInBy:Checked in by`;
    case 'ReleasedAt':
      return $localize`:@@history.field.releasedAt:Released for want of a check-in`;
    case 'DecidedAt':
      return $localize`:@@history.field.decidedAt:Decided`;
    case 'DecidedByUserId':
      return $localize`:@@history.field.decidedBy:Decided by`;
    case 'DeclineReason':
    case 'Reason':
      return $localize`:@@history.field.reason:Reason`;
    case 'ResourceId':
      return $localize`:@@history.field.resource:Resource`;
    case 'UserId':
      return $localize`:@@history.field.person:Person`;
    case 'Response':
      return $localize`:@@history.field.response:Answer`;
    case 'RespondedAt':
      return $localize`:@@history.field.respondedAt:Answered`;
    case 'Name':
      return $localize`:@@history.field.name:Name`;
    case 'ResourceTypeId':
      return $localize`:@@history.field.kind:Kind`;
    case 'Capacity':
      return $localize`:@@history.field.capacity:Capacity`;
    case 'Location':
      return $localize`:@@history.field.location:Location`;
    case 'Description':
      return $localize`:@@history.field.description:Description`;
    case 'CheckInCode':
      return $localize`:@@history.field.qrCode:QR code`;
    case 'DoorScreenTokenHash':
      return $localize`:@@history.field.doorScreen:Door screen`;
    case 'TeamId':
      return $localize`:@@history.field.team:Team of`;
    case 'MaxDurationMinutes':
      return $localize`:@@history.field.maxDuration:Longest booking, minutes`;
    case 'MaxDaysAhead':
      return $localize`:@@history.field.maxDaysAhead:Days ahead at most`;
    case 'MaxActivePerUser':
      return $localize`:@@history.field.maxActive:Bookings per person at most`;
    case 'OfficeHoursOnly':
      return $localize`:@@history.field.officeHoursOnly:Within office hours only`;
    case 'RequiresApproval':
      return $localize`:@@history.field.requiresApproval:Waits for an Admin's approval`;
    case 'TimeZone':
      return $localize`:@@history.field.timeZone:Time zone`;
    case 'OpensAt':
      return $localize`:@@history.field.opensAt:Opens`;
    case 'ClosesAt':
      return $localize`:@@history.field.closesAt:Closes`;
    case 'Workdays':
      return $localize`:@@history.field.workdays:Working days`;
    case 'Category':
      return $localize`:@@history.field.category:About`;
    case 'IsBlocking':
      return $localize`:@@history.field.blocking:Can't be used until fixed`;
    case 'ResolvedAt':
      return $localize`:@@history.field.resolvedAt:Resolved`;
    case 'FirstName':
      return $localize`:@@history.field.firstName:First name`;
    case 'LastName':
      return $localize`:@@history.field.lastName:Last name`;
    case 'Email':
      return $localize`:@@history.field.email:Email`;
    case 'ManagerId':
      return $localize`:@@history.field.manager:Manager`;
    case 'MustChangePassword':
      return $localize`:@@history.field.temporaryPassword:Temporary password`;
    case 'PasswordHash':
      return $localize`:@@history.field.password:Password`;
    default:
      return property;
  }
}

/** A secret says only that it was made, replaced, set up or turned off. */
function secretText(change: AuditChange): string | null {
  if (change.old !== '***' && change.new !== '***') return null;
  if (change.property === 'DoorScreenTokenHash') {
    return change.old === null
      ? $localize`:@@history.secret.setUp:set up`
      : change.new === null
        ? $localize`:@@history.secret.turnedOff:turned off`
        : $localize`:@@history.secret.newLink:given a new link`;
  }
  return change.old === null
    ? $localize`:@@history.secret.made:set`
    : $localize`:@@history.secret.replaced:replaced`;
}

const PEOPLE = new Set(['UserId', 'CheckedInByUserId', 'DecidedByUserId', 'ManagerId', 'TeamId']);

function value(
  entry: AuditEntry,
  property: string,
  raw: string | null,
  context: AuditContext,
): string {
  if (raw === null) return '—';
  if (raw === 'true') return $localize`:@@common.yes:Yes`;
  if (raw === 'false') return $localize`:@@common.no:No`;
  if (PEOPLE.has(property)) return context.people[raw] ?? $localize`:@@common.someone:Someone`;
  if (property === 'ResourceId') return context.resources[raw] ?? raw;
  if (property === 'ResourceTypeId') {
    return context.types[raw] ? resourceTypeLabel(context.types[raw]) : raw;
  }
  if (property === 'Category') return issueCategoryLabel(raw as IssueCategory);
  if (property === 'Response') return attendeeResponseLabel(raw as AttendeeResponse);
  if (property === 'Status') return statusLabel(entry.entityType, raw);
  if (/^\d{4}-\d{2}-\d{2}T/.test(raw)) {
    return formatDate(raw, 'EEE d MMM y, HH:mm', context.locale);
  }
  return raw;
}

function statusLabel(entityType: string, raw: string): string {
  switch (entityType) {
    case 'Reservation':
      return reservationStatusLabel(raw as ReservationStatus);
    case 'Resource':
      return resourceStatusLabel(raw as ResourceStatus);
    case 'IssueReport':
      return issueStatusLabel(raw as IssueStatus);
    case 'User':
      return accountStatusLabel(raw as AccountStatus);
    default:
      return raw;
  }
}

/** A booking's or a block's two UTC instants, as "Mon 19 Oct, 11:00-12:00" in the screen's zone. */
function times(label: string, locale: string): string {
  const [start, end] = label.split(' ');
  if (!start || !end) return label;
  const sameDay = new Date(start).toDateString() === new Date(end).toDateString();
  return `${formatDate(start, 'EEE d MMM y, HH:mm', locale)}-${formatDate(
    end,
    sameDay ? 'HH:mm' : 'EEE d MMM y, HH:mm',
    locale,
  )}`;
}
