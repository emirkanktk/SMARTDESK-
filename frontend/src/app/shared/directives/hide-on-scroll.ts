import { DOCUMENT } from '@angular/common';
import { DestroyRef, Directive, ElementRef, inject, signal } from '@angular/core';

/** A scroll has to go this far before the bar reacts, so a trackpad's small drift doesn't flip it. */
const TOLERANCE_PX = 6;

/**
 * For a sticky bar: it gets out of the way while the page scrolls down, and comes back as soon
 * as it scrolls up, so the navigation is one flick away from anywhere on a long page. It is
 * always there at the top of the page and at the very bottom.
 *
 * This only sets the class `scrolled-away`; the bar's own styles say what that looks like, and
 * should keep the bar in view while it holds focus (a keyboard user tabbing into it, an open
 * menu): `.scrolled-away:not(:focus-within)`.
 */
@Directive({
  selector: '[appHideOnScroll]',
  host: {
    '[class.scrolled-away]': 'away()',
    '(window:scroll)': 'onScroll()',
  },
})
export class HideOnScroll {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly document = inject(DOCUMENT);

  protected readonly away = signal(false);
  /** Where the page was when the bar last reacted; small moves add up until they count. */
  private lastY = 0;
  private frame: number | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      if (this.frame !== null) {
        this.document.defaultView?.cancelAnimationFrame(this.frame);
      }
    });
  }

  protected onScroll(): void {
    const view = this.document.defaultView;
    // At most once a frame, however many scroll events arrive.
    if (view && this.frame === null) {
      this.frame = view.requestAnimationFrame(() => {
        this.frame = null;
        this.update(view);
      });
    }
  }

  private update(view: Window): void {
    // Below zero and past the end are an overscroll bounce (iOS): read as the nearest edge.
    const bottom = this.document.documentElement.scrollHeight - view.innerHeight;
    const y = Math.min(Math.max(0, view.scrollY), Math.max(0, bottom));
    const delta = y - this.lastY;

    if (y <= this.host.nativeElement.offsetHeight || y >= bottom - 1) {
      this.away.set(false);
    } else if (delta > TOLERANCE_PX) {
      this.away.set(true);
    } else if (delta < -TOLERANCE_PX) {
      this.away.set(false);
    } else {
      return;
    }
    this.lastY = y;
  }
}
