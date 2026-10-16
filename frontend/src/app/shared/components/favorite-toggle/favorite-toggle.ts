import { Component, computed, inject, input } from '@angular/core';
import { Resource } from '../../../core/models/resource.model';
import { FavoriteService } from '../../../core/services/favorite.service';
import { Icon } from '../icon/icon';

/**
 * The star that makes a resource one of the user's favourites, or not (ADR-0051): a toggle
 * button, pressed while it is one, named after the resource so a list of them reads well.
 */
@Component({
  imports: [Icon],
  selector: 'app-favorite-toggle',
  template: `
    <button
      type="button"
      class="star"
      [class.on]="on()"
      [attr.aria-pressed]="on()"
      [attr.aria-label]="label()"
      [title]="hint()"
      (click)="toggle($event)"
    >
      <app-icon name="star" />
    </button>
  `,
  styles: `
    :host {
      display: inline-flex;
      vertical-align: middle;
    }
    .star {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 2rem;
      height: 2rem;
      padding: 0;
      border: 0;
      border-radius: 999px;
      background: none;
      color: var(--color-text-muted);
      font-size: 1.125rem;
      cursor: pointer;
    }
    .star:hover {
      background: var(--color-surface-2);
      color: var(--color-warning);
    }
    .star.on {
      --icon-fill: currentColor;
      color: var(--color-warning);
    }
  `,
})
export class FavoriteToggle {
  readonly resource = input.required<Resource>();

  private readonly favorites = inject(FavoriteService);

  protected readonly on = computed(() => this.favorites.ids().has(this.resource().id));
  protected readonly label = computed(
    () => $localize`:@@favorites.toggle:${this.resource().name}:resource: in your favourites`,
  );
  protected readonly hint = computed(() =>
    this.on()
      ? $localize`:@@favorites.remove:Take it off your favourites`
      : $localize`:@@favorites.add:Add it to your favourites`,
  );

  constructor() {
    this.favorites.load();
  }

  protected toggle(event: Event): void {
    // In a row or card that opens something itself, the star does only this.
    event.stopPropagation();
    this.favorites.toggle(this.resource());
  }
}
