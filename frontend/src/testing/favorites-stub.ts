import { Provider, computed, signal } from '@angular/core';
import { Resource } from '../app/core/models/resource.model';
import { FavoriteService } from '../app/core/services/favorite.service';

/**
 * Favourites without HTTP (ADR-0051), as on screen: a star changes the list at once. Specs that
 * look at them set `list`, and check `toggle` and `load`.
 */
export class FakeFavorites {
  readonly list = signal<Resource[]>([]);
  readonly favorites = this.list.asReadonly();
  readonly ids = computed(() => new Set(this.list().map((r) => r.id)));
  readonly load = vi.fn();
  readonly toggle = vi.fn((resource: Resource) =>
    this.list.update((list) =>
      list.some((r) => r.id === resource.id)
        ? list.filter((r) => r.id !== resource.id)
        : [...list, resource],
    ),
  );
}

/** Provides `fake` as the FavoriteService, none starred unless the spec says so. */
export function favoritesStub(fake = new FakeFavorites()): Provider {
  return { provide: FavoriteService, useValue: fake };
}
