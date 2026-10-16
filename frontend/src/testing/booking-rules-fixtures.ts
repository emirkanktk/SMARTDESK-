import {
  BookingRules,
  OfficeHours,
  ResourceTypeRules,
} from '../app/core/models/booking-rules.model';
import { DESK_TYPE } from './resource-fixtures';

/** The browser's own zone, so the office's hours read like the form's local times in any zone. */
export const BROWSER_ZONE = Intl.DateTimeFormat().resolvedOptions().timeZone;

export function fakeOfficeHours(overrides: Partial<OfficeHours> = {}): OfficeHours {
  return {
    timeZone: BROWSER_ZONE,
    opensAt: '09:00:00',
    closesAt: '18:00:00',
    workdays: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'],
    description: 'Mon-Fri 09:00-18:00',
    ...overrides,
  };
}

/** Desks, with no rules of their own unless given. */
export function fakeKindRules(overrides: Partial<ResourceTypeRules> = {}): ResourceTypeRules {
  return {
    resourceTypeId: DESK_TYPE.id,
    name: DESK_TYPE.name,
    maxDurationMinutes: null,
    maxDaysAhead: null,
    maxActivePerUser: null,
    officeHoursOnly: false,
    requiresApproval: false,
    ...overrides,
  };
}

export function fakeBookingRules(
  types: ResourceTypeRules[] = [],
  officeHours: OfficeHours = fakeOfficeHours(),
): BookingRules {
  return {
    officeHours,
    types,
    bounds: { maxDurationMinutes: 44640, maxDaysAhead: 180, maxActivePerUser: 100 },
  };
}
