// Shape of `GET /api/reports/usage` - see docs/API_CONTRACTS.md, Reports.

import { ResourceStatus } from './resource.model';

/** The API refuses a longer range; both ends count as whole local days. */
export const REPORT_MAX_DAYS = 92;

export interface UsageReportQuery {
  /** Local dates such as "2026-10-05", both included. */
  from: string;
  to: string;
  /** IANA zone the days and office hours are read in. */
  timeZone: string;
  resourceTypeId?: string | null;
}

export interface UsageTotals {
  resources: number;
  reservations: number;
  cancelled: number;
  bookedHours: number;
  workingHoursBooked: number;
  workingHoursAvailable: number;
  /** Booked office hours / available office hours, 0 to 1. */
  utilization: number;
}

export interface ResourceUsage {
  resourceId: string;
  resourceName: string;
  resourceTypeId: string;
  resourceTypeName: string;
  status: ResourceStatus;
  reservations: number;
  cancelled: number;
  bookedHours: number;
  workingHoursBooked: number;
  utilization: number;
}

export interface ResourceTypeUsage {
  resourceTypeId: string;
  resourceTypeName: string;
  resources: number;
  reservations: number;
  cancelled: number;
  bookedHours: number;
  workingHoursBooked: number;
  workingHoursAvailable: number;
  utilization: number;
}

export interface UserUsage {
  userId: string;
  displayName: string | null;
  reservations: number;
  bookedHours: number;
}

export interface UsageReport {
  from: string;
  to: string;
  timeZone: string;
  fromUtc: string;
  toUtc: string;
  /** e.g. "Mon-Fri 09:00-18:00" - what utilization is measured against. */
  workingHours: string;
  workingHoursPerResource: number;
  totals: UsageTotals;
  /** Busiest first. */
  resources: ResourceUsage[];
  byType: ResourceTypeUsage[];
  /** At most 5, most booked hours first. */
  topUsers: UserUsage[];
}
