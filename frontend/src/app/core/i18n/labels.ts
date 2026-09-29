import { Role, Roles } from '../constants/roles';
import { AccountStatus } from '../models/account.model';
import { ReservationStatus } from '../models/reservation.model';
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
