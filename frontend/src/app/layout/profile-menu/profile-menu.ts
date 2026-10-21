import {
  Component,
  ElementRef,
  LOCALE_ID,
  computed,
  inject,
  signal,
  viewChild,
  viewChildren,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Roles } from '../../core/constants/roles';
import { roleLabel } from '../../core/i18n/labels';
import { AuthService } from '../../core/services/auth.service';
import { PushService } from '../../core/services/push.service';
import { Icon } from '../../shared/components/icon/icon';

/**
 * The signed-in user in the top bar or at the bottom of the sidebar: their initials, name and
 * role, opening a menu with their profile, the password page and signing out. A menu button as
 * WAI-ARIA describes it: the arrow keys move through the items, Escape closes it and gives
 * focus back to the button, and a click anywhere else closes it.
 */
@Component({
  selector: 'app-profile-menu',
  imports: [RouterLink, Icon],
  templateUrl: './profile-menu.html',
  styleUrl: './profile-menu.scss',
  host: {
    '(document:click)': 'onDocumentClick($event)',
    '(keydown.escape)': 'closeAndFocus()',
  },
})
export class ProfileMenu {
  private readonly authService = inject(AuthService);
  private readonly push = inject(PushService);
  private readonly router = inject(Router);
  private readonly locale = inject(LOCALE_ID);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  private readonly trigger = viewChild.required<ElementRef<HTMLButtonElement>>('trigger');
  private readonly items = viewChildren<ElementRef<HTMLElement>>('item');

  protected readonly open = signal(false);
  protected readonly displayName = this.authService.displayName;
  protected readonly email = computed(() => this.authService.currentUser()?.email ?? '');

  /** "AY" for Ayşe Yılmaz; the email's first letter when there's no name. */
  protected readonly initials = computed(() => {
    const user = this.authService.currentUser();
    if (!user) {
      return '';
    }
    const letters = [user.firstName, user.lastName]
      .map((part) => (part ?? '').trim().charAt(0))
      .join('');
    return (letters || (user.email ?? '').charAt(0)).toLocaleUpperCase(this.locale);
  });

  /** What a screen reader says for the button, since the initials are only a picture. */
  protected readonly buttonLabel = computed(() => {
    const role = this.roleBadge();
    return role
      ? $localize`:@@profileMenu.openWithRole:Account menu: ${this.displayName()}:name:, ${role}:role:`
      : $localize`:@@profileMenu.open:Account menu: ${this.displayName()}:name:`;
  });

  /** "Admin" or "Manager" next to the name; nothing for an Employee. */
  protected readonly roleBadge = computed(() =>
    this.authService.isAdmin()
      ? roleLabel(Roles.Admin)
      : this.authService.isManager()
        ? roleLabel(Roles.Manager)
        : null,
  );

  protected toggle(): void {
    if (this.open()) {
      this.open.set(false);
    } else {
      this.openAt(0);
    }
  }

  /** Down arrow on the button opens the menu at its first item, up arrow at its last. */
  protected openAt(index: number, event?: Event): void {
    event?.preventDefault();
    this.open.set(true);
    // The items exist once the menu has rendered.
    setTimeout(() => this.focusItem(index < 0 ? this.items().length - 1 : index));
  }

  protected onMenuKeydown(event: KeyboardEvent): void {
    const items = this.items().map((item) => item.nativeElement);
    const current = items.indexOf(document.activeElement as HTMLElement);
    const moves: Record<string, number> = {
      ArrowDown: (current + 1) % items.length,
      ArrowUp: (current - 1 + items.length) % items.length,
      Home: 0,
      End: items.length - 1,
    };
    if (event.key in moves) {
      event.preventDefault();
      this.focusItem(moves[event.key]);
    } else if (event.key === 'Tab') {
      // Moving on: the menu closes and the browser takes focus to the next thing.
      this.open.set(false);
    }
  }

  protected close(): void {
    this.open.set(false);
  }

  protected closeAndFocus(): void {
    if (this.open()) {
      this.open.set(false);
      this.trigger().nativeElement.focus();
    }
  }

  /**
   * First this device stops notifying whoever signs out (ADR-0056), while their session can still
   * tell the server; then the sign-out, whatever came of that.
   */
  protected signOut(): void {
    this.open.set(false);
    void this.push.forgetThisDevice().then(() => {
      this.authService.logout();
      void this.router.navigateByUrl('/');
    });
  }

  protected onDocumentClick(event: MouseEvent): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node)) {
      this.open.set(false);
    }
  }

  private focusItem(index: number): void {
    this.items()[index]?.nativeElement.focus();
  }
}
