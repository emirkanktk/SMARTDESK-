import { DOCUMENT } from '@angular/common';
import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationStart, Router, RouterLink, RouterLinkActive } from '@angular/router';
import { filter } from 'rxjs';
import { Icon } from '../../shared/components/icon/icon';
import { NavItem } from '../nav-item';

/**
 * A phone's way around the app (ADR-0055), at the bottom where a thumb reaches: the screens used
 * most on the move - the dashboard, the floor plan, booking and one's own bookings - and Menu,
 * which opens every screen this user may open in a sheet from below. Hidden on wider screens,
 * which have the shell's bar or sidebar. `has-tab-bar` on <body> moves what's fixed at the
 * bottom - toasts, the assistant's button - up above it (styles.scss).
 */
@Component({
  selector: 'app-tab-bar',
  imports: [Icon, RouterLink, RouterLinkActive],
  styleUrl: './tab-bar.scss',
  templateUrl: './tab-bar.html',
  host: { '(document:keydown.escape)': 'close(true)' },
})
export class TabBar {
  /** Every screen this user may open, as the shell lists them. */
  readonly items = input.required<readonly NavItem[]>();

  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  private readonly menuButton = viewChild.required<ElementRef<HTMLButtonElement>>('menuButton');
  private readonly sheet = viewChild<ElementRef<HTMLElement>>('sheet');

  protected readonly open = signal(false);

  /** Something in the menu has a number, unread messages say: a dot on Menu shows it. */
  protected readonly attention = computed(() =>
    this.items().some((item) => (item.count?.() ?? 0) > 0),
  );

  constructor() {
    const body = this.document.body;
    body.classList.add('has-tab-bar');
    inject(DestroyRef).onDestroy(() => body.classList.remove('has-tab-bar'));
    inject(Router)
      .events.pipe(
        filter((event) => event instanceof NavigationStart),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.open.set(false));
  }

  protected toggle(): void {
    if (this.open()) {
      this.close(true);
      return;
    }
    this.open.set(true);
    afterNextRender(() => this.focusable()[0]?.focus(), { injector: this.injector });
  }

  /** Closes the menu; `refocus` puts the focus back on Menu, as after Escape. */
  protected close(refocus = false): void {
    if (!this.open()) {
      return;
    }
    this.open.set(false);
    if (refocus) {
      this.menuButton().nativeElement.focus();
    }
  }

  /** The sheet covers the page, so Tab goes round inside it. */
  protected keepFocusIn(event: KeyboardEvent): void {
    if (event.key !== 'Tab') {
      return;
    }
    const focusable = this.focusable();
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    const active = this.document.activeElement;
    if (event.shiftKey && active === first) {
      event.preventDefault();
      last?.focus();
    } else if (!event.shiftKey && active === last) {
      event.preventDefault();
      first?.focus();
    }
  }

  private focusable(): HTMLElement[] {
    return Array.from(this.sheet()?.nativeElement.querySelectorAll<HTMLElement>('a, button') ?? []);
  }
}
