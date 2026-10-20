import { Signal } from '@angular/core';
import { Role } from '../core/constants/roles';
import { IconName } from '../shared/components/icon/icon';

/** A screen in the app's navigation: the shell's bar or sidebar, and a phone's menu (ADR-0055). */
export interface NavItem {
  path: string;
  label: string;
  icon: IconName;
  /** Shown only to these roles; everyone sees it when left out. */
  roles?: readonly Role[];
  /** A number next to the label while it isn't 0, e.g. unread messages. */
  count?: Signal<number>;
  /** What the number counts, for screen readers: unread messages unless it says otherwise. */
  counts?: 'new';
}
