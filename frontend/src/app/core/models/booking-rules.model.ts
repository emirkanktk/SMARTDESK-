// Shapes of the booking rules endpoints - see docs/API_CONTRACTS.md, Booking rules (ADR-0046).
import { WeekDay } from './reservation.model';

/** When the office is open, on the clock of its own time zone. */
export interface OfficeHours {
  /** IANA, e.g. "Europe/Istanbul". */
  timeZone: string;
  /** "09:00:00"; the API also takes "09:00". */
  opensAt: string;
  closesAt: string;
  workdays: WeekDay[];
  /** "Mon-Fri 09:00-18:00", in the app's language. */
  description: string;
}

/** A kind of resource's own rules. A rule that's null isn't set: only the general one applies. */
export interface ResourceTypeRules {
  resourceTypeId: string;
  /** As the API names the kind ("Desk"); `resourceTypeLabel` translates it. */
  name: string;
  maxDurationMinutes: number | null;
  /** How many days after today a booking may start: 0 is today only. */
  maxDaysAhead: number | null;
  /** How many bookings of the kind one person may have that haven't ended. */
  maxActivePerUser: number | null;
  officeHoursOnly: boolean;
}

/** The most a kind's rules may be set to. */
export interface RuleBounds {
  maxDurationMinutes: number;
  maxDaysAhead: number;
  maxActivePerUser: number;
}

export interface BookingRules {
  officeHours: OfficeHours;
  types: ResourceTypeRules[];
  bounds: RuleBounds;
}

export type OfficeHoursRequest = Omit<OfficeHours, 'description'>;
export type ResourceTypeRulesRequest = Omit<ResourceTypeRules, 'resourceTypeId' | 'name'>;

/** A 409's code: the owner has as many bookings of the kind as its rules allow. */
export const BOOKING_LIMIT = 'booking-limit';

/** Durations and office hours go in quarter hours, the steps the booking form works in. */
export const RULE_STEP_MINUTES = 15;
