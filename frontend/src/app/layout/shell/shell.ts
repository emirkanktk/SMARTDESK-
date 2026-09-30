import { Component, LOCALE_ID, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Role, Roles } from '../../core/constants/roles';
import { roleLabel } from '../../core/i18n/labels';
import { AuthService } from '../../core/services/auth.service';
import { Icon, IconName } from '../../shared/components/icon/icon';
import { LanguageSwitch } from '../../shared/components/language-switch/language-switch';
import { Logo } from '../../shared/components/logo/logo';
import { ThemeSwitch } from '../../shared/components/theme-switch/theme-switch';

interface NavItem {
  path: string;
  label: string;
  icon: IconName;
  /** Shown only to these roles; everyone sees it when left out. */
  roles?: readonly Role[];
}

/**
 * Frame around every signed-in screen: the navigation, the user and sign-out. A sidebar on
 * wide screens; on narrower ones a bar on top, its links in a row that scrolls sideways.
 */
@Component({
  imports: [RouterLink, RouterLinkActive, RouterOutlet, Icon, LanguageSwitch, Logo, ThemeSwitch],
  selector: 'app-shell',
  styleUrl: './shell.scss',
  templateUrl: './shell.html',
})
export class Shell {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly locale = inject(LOCALE_ID);

  protected readonly displayName = this.authService.displayName;

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

  /** "Admin" or "Manager" next to the name; nothing for an Employee. */
  protected readonly roleBadge = computed(() =>
    this.authService.isAdmin()
      ? roleLabel(Roles.Admin)
      : this.authService.isManager()
        ? roleLabel(Roles.Manager)
        : null,
  );

  /**
   * For the avatar: "AY" for Ayşe Yılmaz, the email's first letter when there's no name. A
   * token may leave any of them out.
   */
  protected readonly initials = computed(() => {
    const user = this.authService.currentUser();
    if (!user) return '';
    const letters = [user.firstName, user.lastName].map((part) => part?.trim().charAt(0) ?? '');
    return (letters.join('') || user.email?.charAt(0) || '').toLocaleUpperCase(this.locale);
  });

  protected logout(): void {
    this.authService.logout();
    void this.router.navigateByUrl('/');
  }
}
