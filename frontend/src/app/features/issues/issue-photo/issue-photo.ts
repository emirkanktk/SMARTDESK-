import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { IssueService } from '../../../core/services/issue.service';

/**
 * A reported problem's photo (ADR-0037), fetched with the token like a resource's (ADR-0027)
 * and shown through an object URL: a thumbnail that opens the whole picture in a new tab.
 */
@Component({
  selector: 'app-issue-photo',
  template: `
    @if (url(); as src) {
      <a class="photo" [href]="src" target="_blank" rel="noopener" [attr.aria-label]="label()">
        <img [src]="src" alt="" />
      </a>
    } @else if (failed()) {
      <span class="text-muted failed" i18n="@@issues.photoFailed"
        >The photo could not be loaded.</span
      >
    }
  `,
  styles: `
    :host {
      display: block;
    }
    .photo {
      display: block;
      width: 6rem;
      height: 4.5rem;
      overflow: hidden;
      border-radius: var(--radius);
      border: 1px solid var(--color-border);
    }
    img {
      display: block;
      width: 100%;
      height: 100%;
      object-fit: cover;
    }
    .failed {
      font-size: 0.875rem;
    }
  `,
})
export class IssuePhoto {
  readonly issueId = input.required<string>();
  /** What the link opens, read out in the picture's place. */
  readonly label = input.required<string>();

  private readonly issues = inject(IssueService);

  protected readonly url = signal<string | null>(null);
  protected readonly failed = signal(false);

  constructor() {
    effect(() => {
      const id = this.issueId();
      untracked(() =>
        this.issues.getPhoto(id).subscribe({
          next: (blob) => {
            this.revoke();
            this.url.set(URL.createObjectURL(blob));
          },
          error: () => this.failed.set(true),
        }),
      );
    });
    inject(DestroyRef).onDestroy(() => this.revoke());
  }

  private revoke(): void {
    const current = this.url();
    if (current) {
      URL.revokeObjectURL(current);
    }
  }
}
