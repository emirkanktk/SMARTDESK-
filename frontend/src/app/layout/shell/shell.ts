import { Component, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Role, Roles } from '../../core/constants/roles';
import { AuthService } from '../../core/services/auth.service';

interface NavItem {
  path: string;
  label: string;
  /** Shown only to these roles; everyone sees it when left out. */
  roles?: readonly Role[];
}

/** Frame around every signed-in screen: top bar with navigation, user and sign-out. */
@Component({
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  selector: 'app-shell',
  styleUrl: './shell.scss',
  templateUrl: './shell.html',
})
export class Shell {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly displayName = this.authService.displayName;

  private readonly navItems: readonly NavItem[] = [
    { path: '/dashboard', label: 'Dashboard' },
    { path: '/resources', label: 'Resources' },
    { path: '/availability', label: 'Availability' },
    { path: '/calendar', label: 'Calendar' },
    { path: '/reservations/new', label: 'New reservation' },
    { path: '/reservations/mine', label: 'My reservations' },
    { path: '/team', label: 'My team', roles: [Roles.Manager] },
    { path: '/admin', label: 'Admin', roles: [Roles.Admin] },
    { path: '/admin/users', label: 'Users', roles: [Roles.Admin] },
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
    this.authService.isAdmin() ? Roles.Admin : this.authService.isManager() ? Roles.Manager : null,
  );

  protected logout(): void {
    this.authService.logout();
    void this.router.navigateByUrl('/');
  }
}
