import { Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Role, Roles } from '../../core/constants/roles';
import { AuthService } from '../../core/services/auth.service';
import { LanguageSwitch } from '../../shared/components/language-switch/language-switch';
import { ThemeSwitch } from '../../shared/components/theme-switch/theme-switch';
import { ProfileMenu } from '../profile-menu/profile-menu';

interface NavItem {
  path: string;
  label: string;
  /** Shown only to these roles; everyone sees it when left out. */
  roles?: readonly Role[];
}

/**
 * Frame around every signed-in screen: the top bar with the navigation, the language and
 * theme switches, and the user's menu (profile, password, sign out).
 */
@Component({
  imports: [RouterLink, RouterLinkActive, RouterOutlet, LanguageSwitch, ThemeSwitch, ProfileMenu],
  selector: 'app-shell',
  styleUrl: './shell.scss',
  templateUrl: './shell.html',
})
export class Shell {
  private readonly authService = inject(AuthService);

  private readonly navItems: readonly NavItem[] = [
    { path: '/dashboard', label: $localize`:@@nav.dashboard:Dashboard` },
    { path: '/resources', label: $localize`:@@nav.resources:Resources` },
    { path: '/availability', label: $localize`:@@nav.availability:Availability` },
    { path: '/calendar', label: $localize`:@@nav.calendar:Calendar` },
    { path: '/reservations/new', label: $localize`:@@nav.newReservation:New reservation` },
    { path: '/reservations/mine', label: $localize`:@@nav.myReservations:My reservations` },
    { path: '/team', label: $localize`:@@nav.team:My team`, roles: [Roles.Manager] },
    { path: '/admin', label: $localize`:@@nav.admin:Admin`, roles: [Roles.Admin] },
    { path: '/admin/users', label: $localize`:@@nav.users:Users`, roles: [Roles.Admin] },
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
