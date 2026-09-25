// Shape of `GET /api/dashboard` - see docs/API_CONTRACTS.md, Dashboard.

import { Reservation } from './reservation.model';

export interface DashboardStats {
  activeResources: number;
  resourcesInUseNow: number;
  reservationsToday: number;
  /** The full count; `myUpcoming` itself is capped. */
  myUpcomingReservations: number;
}

export interface Dashboard {
  generatedAt: string;
  /** The IANA zone "today" was computed in. */
  timeZone: string;
  /** Local midnight and the next one, as UTC ISO strings (23 or 25 h apart on DST days). */
  todayStart: string;
  todayEnd: string;
  stats: DashboardStats;
  /** Every Confirmed reservation overlapping today, all resources, earliest first. */
  today: Reservation[];
  /** Confirmed reservations running right now, ending soonest first. */
  inUseNow: Reservation[];
  /** The caller's next Confirmed reservations that haven't started, at most 5. */
  myUpcoming: Reservation[];
}
