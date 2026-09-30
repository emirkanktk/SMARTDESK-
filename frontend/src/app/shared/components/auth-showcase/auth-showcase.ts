import { Component } from '@angular/core';
import { Icon } from '../icon/icon';
import { Logo } from '../logo/logo';

/**
 * The coloured half of the signed-out account screens on wide screens: the logo, what
 * SmartDesk is for, and a booking floating by. Decoration next to the form, so screen readers
 * skip it; narrow screens leave it out (shared/styles/_auth-page.scss).
 */
@Component({
  selector: 'app-auth-showcase',
  imports: [Icon, Logo],
  host: { class: 'brand-panel', 'aria-hidden': 'true' },
  template: `
    <app-logo [light]="true" />

    <div class="pitch">
      <h2 i18n="@@auth.showcase.title">Your office, booked in seconds.</h2>
      <p i18n="@@auth.showcase.text">
        Meeting rooms, desks, cars and equipment: see what's free and make it yours.
      </p>
      <ul class="points">
        <li>
          <app-icon name="shield" /><span i18n="@@auth.showcase.noDouble"
            >No double bookings, ever</span
          >
        </li>
        <li>
          <app-icon name="zap" /><span i18n="@@auth.showcase.live">The calendar updates live</span>
        </li>
        <li>
          <app-icon name="bell" /><span i18n="@@auth.showcase.reminders"
            >A reminder before each booking</span
          >
        </li>
      </ul>
    </div>

    <div class="note">
      <span class="note-icon"><app-icon name="calendar-check" /></span>
      <span class="note-text">
        <strong>Meeting Room A</strong>
        <span>09:30-10:30 · <span i18n="@@home.preview.sprintPlanning">Sprint planning</span></span>
      </span>
    </div>
  `,
  styles: `
    /* The gradient, the white text and the light in its corners come from .brand-panel. */
    :host {
      display: flex;
      flex-direction: column;
      justify-content: space-between;
      gap: var(--space-6);
      padding: var(--space-7) var(--space-6);
      font-size: 1.25rem;
    }

    .pitch {
      font-size: 1rem;
    }
    h2 {
      margin: 0 0 var(--space-3);
      font-size: clamp(1.75rem, 1.1rem + 1.6vw, 2.5rem);
      font-weight: 800;
      line-height: 1.1;
      letter-spacing: -0.03em;
    }
    p {
      max-width: 28rem;
      margin: 0 0 var(--space-6);
      color: rgb(255 255 255 / 85%);
      font-size: 1.0625rem;
    }

    .points {
      display: grid;
      gap: var(--space-3);
      margin: 0;
      padding: 0;
      list-style: none;
      font-weight: 600;
    }
    li {
      display: flex;
      align-items: center;
      gap: var(--space-3);
    }
    li app-icon {
      width: 2.25rem;
      height: 2.25rem;
      padding: 0.5rem;
      border-radius: 10px;
      background: rgb(255 255 255 / 12%);
      box-shadow: inset 0 0 0 1px rgb(255 255 255 / 18%);
    }

    .note {
      align-self: flex-end;
      display: flex;
      align-items: center;
      gap: var(--space-3);
      padding: var(--space-3) var(--space-4);
      border: 1px solid rgb(255 255 255 / 22%);
      border-radius: var(--radius-lg);
      background: rgb(255 255 255 / 12%);
      backdrop-filter: blur(8px);
      font-size: 0.875rem;
      animation: float 6s ease-in-out infinite;
    }
    .note-icon {
      display: grid;
      place-items: center;
      width: 2.25rem;
      height: 2.25rem;
      border-radius: 10px;
      background: linear-gradient(135deg, #10b981, #0d9488);
      font-size: 1.1rem;
    }
    .note-text {
      display: flex;
      flex-direction: column;
      line-height: 1.35;
    }
    .note-text > span {
      color: rgb(255 255 255 / 85%);
    }

    @keyframes float {
      50% {
        transform: translateY(-8px);
      }
    }
  `,
})
export class AuthShowcase {}
