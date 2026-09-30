import { Component, computed, inject, input } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { map, of, switchMap } from 'rxjs';
import { ResourcePhotoService } from '../../../core/services/resource-photo.service';

/**
 * A resource's photo, or its type's picture when it has none (ADR-0027). It fills the box the
 * parent gives it (a size, or a width and an `aspect-ratio`), cropping the picture to fit.
 */
@Component({
  selector: 'app-resource-photo',
  template: `
    @if (src(); as url) {
      <img [src]="url" [alt]="alt()" loading="lazy" decoding="async" />
    }
  `,
  styles: `
    :host {
      display: block;
      overflow: hidden;
      border-radius: var(--radius);
      background: var(--color-bg);
    }
    img {
      display: block;
      width: 100%;
      height: 100%;
      object-fit: cover;
    }
  `,
})
export class ResourcePhoto {
  /** Null for a resource that isn't saved yet. */
  readonly resourceId = input<string | null | undefined>(null);
  readonly typeName = input.required<string>();
  readonly photoUpdatedAt = input<string | null>(null);
  /** Empty when the resource's name is shown next to it, as it usually is. */
  readonly alt = input('');

  private readonly photos = inject(ResourcePhotoService);

  private readonly photo = computed(
    () => {
      const id = this.resourceId();
      const version = this.photoUpdatedAt();
      return id && version ? { id, version } : null;
    },
    { equal: (a, b) => a?.id === b?.id && a?.version === b?.version },
  );

  private readonly loaded = toSignal(
    toObservable(this.photo).pipe(
      switchMap((photo) =>
        photo
          ? this.photos.photoUrl(photo.id, photo.version).pipe(map((url) => ({ photo, url })))
          : of(null),
      ),
    ),
    { initialValue: null },
  );

  /** Nothing while the photo loads, so the type's picture doesn't flash up first. */
  protected readonly src = computed(() => {
    const photo = this.photo();
    if (!photo) {
      return this.photos.typeImage(this.typeName());
    }
    const loaded = this.loaded();
    if (loaded?.photo !== photo) {
      return null;
    }
    return loaded.url ?? this.photos.typeImage(this.typeName());
  });
}
