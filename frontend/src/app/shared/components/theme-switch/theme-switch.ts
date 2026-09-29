import { DOCUMENT } from '@angular/common';
import { Component, computed, inject } from '@angular/core';
import { ThemeService } from '../../../core/theme/theme.service';

type TransitionDocument = Document & {
  startViewTransition?: (update: () => void) => { ready: Promise<void> };
};

/**
 * Light or dark, next to the language switch in the top bar, on the front page and on
 * sign-in. A small switch: the knob slides across, its sun turns into a moon, stars come out,
 * and the new theme spreads over the page in a circle from where it was clicked. With
 * "reduce motion" on, or in a browser without view transitions, it just switches. For a
 * screen reader it's a toggle named "Dark mode", pressed while dark.
 */
@Component({
  selector: 'app-theme-switch',
  template: `
    <button
      type="button"
      class="theme-switch"
      aria-label="Dark mode"
      i18n-aria-label="@@theme.dark"
      [attr.aria-pressed]="dark()"
      [title]="dark() ? toLight : toDark"
      (click)="toggle($event)"
    >
      <span class="track" aria-hidden="true">
        <span class="star star-1"></span>
        <span class="star star-2"></span>
        <span class="star star-3"></span>
        <span class="knob">
          <svg viewBox="0 0 24 24" focusable="false">
            <g class="sun">
              <circle cx="12" cy="12" r="4" />
              <path
                d="M12 2.5v2M12 19.5v2M2.5 12h2M19.5 12h2M5.3 5.3l1.4 1.4M17.3 17.3l1.4 1.4M5.3 18.7l1.4-1.4M17.3 6.7l1.4-1.4"
              />
            </g>
            <path class="moon" d="M19.5 14.2A8 8 0 0 1 9.8 4.5a8 8 0 1 0 9.7 9.7Z" />
          </svg>
        </span>
      </span>
    </button>
  `,
  styles: `
    .theme-switch {
      display: inline-flex;
      padding: 0;
      border: 0;
      border-radius: 999px;
      background: none;
      cursor: pointer;
    }
    .theme-switch:focus-visible {
      outline: 2px solid var(--color-primary);
      outline-offset: 2px;
    }

    .track {
      position: relative;
      display: block;
      width: 3.25rem;
      height: 1.75rem;
      border: 1px solid var(--color-border);
      border-radius: 999px;
      background: var(--color-primary-soft);
      overflow: hidden;
      transition: background-color 400ms ease;
    }

    .knob {
      position: absolute;
      top: 2px;
      left: 2px;
      display: grid;
      place-items: center;
      width: calc(1.75rem - 6px);
      height: calc(1.75rem - 6px);
      border-radius: 50%;
      background: var(--color-surface);
      box-shadow: 0 1px 3px rgb(0 0 0 / 25%);
      transition: transform 400ms cubic-bezier(0.34, 1.4, 0.64, 1);
    }

    svg {
      width: 1rem;
      height: 1rem;
      overflow: visible;
    }
    .sun,
    .moon {
      transform-origin: 12px 12px;
      transition:
        transform 400ms ease,
        opacity 250ms ease;
    }
    .sun {
      fill: none;
      stroke: var(--color-warning);
      stroke-width: 2;
      stroke-linecap: round;
    }
    .sun circle {
      fill: var(--color-warning);
    }
    .moon {
      fill: var(--color-primary);
      opacity: 0;
      transform: rotate(-90deg) scale(0.4);
    }

    .star {
      position: absolute;
      width: 2px;
      height: 2px;
      border-radius: 50%;
      background: var(--color-text);
      opacity: 0;
      transform: scale(0);
      transition:
        opacity 300ms ease,
        transform 300ms ease;
    }
    .star-1 {
      top: 6px;
      left: 9px;
    }
    .star-2 {
      top: 14px;
      left: 15px;
      transition-delay: 60ms;
    }
    .star-3 {
      top: 8px;
      left: 20px;
      transition-delay: 120ms;
    }

    /* Dark: the knob on the right, a moon, stars. Keyed to the page's theme itself, so the
       new state is drawn at once, inside the page transition. */
    :host-context([data-theme='dark']) .track {
      background: var(--color-bg);
    }
    :host-context([data-theme='dark']) .knob {
      transform: translateX(1.5rem);
    }
    :host-context([data-theme='dark']) .sun {
      opacity: 0;
      transform: rotate(90deg) scale(0.4);
    }
    :host-context([data-theme='dark']) .moon {
      opacity: 1;
      transform: rotate(0deg) scale(1);
    }
    :host-context([data-theme='dark']) .star {
      opacity: 0.9;
      transform: scale(1);
    }

    @media (prefers-reduced-motion: reduce) {
      .track,
      .knob,
      .sun,
      .moon,
      .star {
        transition: none;
      }
    }
  `,
})
export class ThemeSwitch {
  private readonly document = inject(DOCUMENT) as TransitionDocument;
  protected readonly themes = inject(ThemeService);
  protected readonly dark = computed(() => this.themes.theme() === 'dark');
  protected readonly toDark = $localize`:@@theme.toDark:Switch to dark mode`;
  protected readonly toLight = $localize`:@@theme.toLight:Switch to light mode`;

  protected toggle(event: MouseEvent): void {
    const view = this.document.defaultView;
    const reduceMotion = view?.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
    if (!this.document.startViewTransition || reduceMotion || !view) {
      this.themes.toggle();
      return;
    }

    // From the pointer, or from the button's middle when a key pressed it.
    const button = (event.currentTarget as HTMLElement).getBoundingClientRect();
    const x = event.detail > 0 ? event.clientX : button.left + button.width / 2;
    const y = event.detail > 0 ? event.clientY : button.top + button.height / 2;
    const radius = Math.hypot(Math.max(x, view.innerWidth - x), Math.max(y, view.innerHeight - y));

    const transition = this.document.startViewTransition(() => this.themes.toggle());
    transition.ready
      .then(() =>
        this.document.documentElement.animate(
          { clipPath: [`circle(0px at ${x}px ${y}px)`, `circle(${radius}px at ${x}px ${y}px)`] },
          { duration: 550, easing: 'ease-in-out', pseudoElement: '::view-transition-new(root)' },
        ),
      )
      .catch(() => undefined);
  }
}
