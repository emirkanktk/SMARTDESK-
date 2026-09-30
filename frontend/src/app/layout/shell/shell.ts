import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Role, Roles } from '../../core/constants/roles';
import { AuthService } from '../../core/services/auth.service';
import { Icon, IconName } from '../../shared/components/icon/icon';
import { HideOnScroll } from '../../shared/directives/hide-on-scroll';
import { LanguageSwitch } from '../../shared/components/language-switch/language-switch';
import { Logo } from '../../shared/components/logo/logo';
import { ThemeSwitch } from '../../shared/components/theme-switch/theme-switch';
import { Assistant } from '../assistant/assistant';
import { ProfileMenu } from '../profile-menu/profile-menu';

interface NavItem {
  path: string;
  label: string;
  icon: IconName;
  /** Shown only to these roles; everyone sees it when left out. */
  roles?: readonly Role[];
}

/**
 * Frame around every signed-in screen: the navigation, the language and theme switches, and
 * the user's menu (profile, password, sign out). A sidebar on wide screens, with the user's
 * card at the bottom; on narrower ones a bar on top, its links in a row that scrolls sideways.
 * That bar gets out of the way while the page scrolls down and is back on the way up. The
 * assistant's button sits in the bottom corner when the server has one (ADR-0029).
 */
@Component({
  imports: [
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    Assistant,
    HideOnScroll,
    Icon,
    LanguageSwitch,
    Logo,
    ProfileMenu,
    ThemeSwitch,
  ],
  selector: 'app-shell',
  styleUrl: './shell.scss',
  templateUrl: './shell.html',
})
export class Shell {
  private readonly authService = inject(AuthService);

  /** The assistant's button is showing: the page leaves room below its end for it. */
  protected readonly assistantShown = signal(false);

  private readonly navItems: readonly NavItem[] = [
    { path: '/dashboard', label: $localize`:@@nav.dashboard:Dashboard`, icon: 'dashboard' },
    { path: '/resources', label: $localize`:@@nav.resources:Resources`, icon: 'building' },
    { path: '/availability', label: $localize`:@@nav.availability:Availability`, icon: 'search' },
    { path: '/calendar', label: $localize`:@@nav.calendar:Calendar`, icon: 'calendar' },
    {
      path: '/reservations/new',
      label: $localize`:@@nav.newReservation:New reservation`,
      icon: 'calendar-plus',
    },
    {
      path: '/reservations/mine',
      label: $localize`:@@nav.myReservations:My reservations`,
      icon: 'clipboard',
    },
    { path: '/team', label: $localize`:@@nav.team:My team`, icon: 'team', roles: [Roles.Manager] },
    { path: '/admin', label: $localize`:@@nav.admin:Admin`, icon: 'sliders', roles: [Roles.Admin] },
    {
      path: '/admin/users',
      label: $localize`:@@nav.users:Users`,
      icon: 'id-card',
      roles: [Roles.Admin],
    },
  ];

  /** What this user's roles may open. The routes' guards and the API check again. */
  protected readonly visibleNavItems = computed(() => {
    // Read the user so this recomputes after sign-in or a role change.
    this.authService.currentUser();
    return this.navItems.filter(
      (item) => !item.roles || item.roles.some((role) => this.authService.hasRole(role)),
    );
  });
}
