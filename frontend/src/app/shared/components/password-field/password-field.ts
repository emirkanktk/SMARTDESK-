import { AfterContentInit, Component, ElementRef, OnDestroy, inject, signal } from '@angular/core';

/**
 * A password input with an eye button that shows what's been typed:
 *
 *   <app-password-field><input id="password" type="password" ... /></app-password-field>
 *
 * The input keeps its form control and attributes. It's hidden again when its form is sent,
 * so a password manager sees a password field and nothing stays on screen afterwards.
 */
@Component({
  selector: 'app-password-field',
  template: `
    <ng-content />
    <button
      type="button"
      class="reveal"
      [attr.aria-controls]="inputId"
      [attr.aria-pressed]="shown()"
      [attr.aria-label]="label"
      [title]="label"
      (click)="toggle()"
    >
      <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
        <path d="M2.5 12S6 5.5 12 5.5 21.5 12 21.5 12 18 18.5 12 18.5 2.5 12 2.5 12Z" />
        <circle cx="12" cy="12" r="3" />
        @if (shown()) {
          <path class="strike" d="M4 20 20 4" />
        }
      </svg>
    </button>
  `,
  host: { class: 'password-field' },
  styles: `
    .reveal {
      position: absolute;
      top: 50%;
      right: var(--space-1);
      display: grid;
      place-items: center;
      width: 2.25rem;
      height: 2.25rem;
      padding: 0;
      border: 0;
      border-radius: var(--radius);
      background: none;
      color: var(--color-text-muted);
      cursor: pointer;
      transform: translateY(-50%);
    }
    .reveal:hover {
      background: var(--color-bg);
      color: var(--color-text);
    }
    .reveal:focus-visible {
      outline: 2px solid var(--color-primary);
      outline-offset: 1px;
    }
    svg {
      width: 1.25rem;
      height: 1.25rem;
      fill: none;
      stroke: currentColor;
      stroke-width: 1.75;
      stroke-linecap: round;
      stroke-linejoin: round;
    }
  `,
})
export class PasswordField implements AfterContentInit, OnDestroy {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly shown = signal(false);
  /** A toggle keeps its name; whether the password shows is its pressed state. */
  protected readonly label = $localize`:@@passwordField.show:Show password`;
  protected inputId: string | null = null;

  private input: HTMLInputElement | null = null;
  private form: HTMLFormElement | null = null;
  private readonly hide = () => this.show(false);

  ngAfterContentInit(): void {
    this.input = this.host.nativeElement.querySelector('input');
    this.inputId = this.input?.id || null;
    this.form = this.input?.form ?? null;
    // Capturing, so it runs before the form's own submit handling.
    this.form?.addEventListener('submit', this.hide, true);
  }

  ngOnDestroy(): void {
    this.form?.removeEventListener('submit', this.hide, true);
  }

  protected toggle(): void {
    this.show(!this.shown());
  }

  private show(shown: boolean): void {
    this.shown.set(shown);
    if (this.input) {
      this.input.type = shown ? 'text' : 'password';
    }
  }
}
