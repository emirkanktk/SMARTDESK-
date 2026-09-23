import { Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';

interface NavItem {
  path: string;
  label: string;
  adminOnly?: boolean;
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
  protected readonly isAdmin = this.authService.isAdmin;

  protected readonly navItems: readonly NavItem[] = [
    { path: '/dashboard', label: 'Dashboard' },
    { path: '/resources', label: 'Resources' },
    { path: '/availability', label: 'Availability' },
    { path: '/reservations/new', label: 'New reservation' },
    { path: '/reservations/mine', label: 'My reservations' },
    { path: '/admin', label: 'Admin', adminOnly: true },
  ];

  protected logout(): void {
    this.authService.logout();
    void this.router.navigateByUrl('/login');
  }
}
