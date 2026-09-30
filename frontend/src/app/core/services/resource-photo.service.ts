import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { EMPTY, Observable, catchError, map, of, shareReplay } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SKIP_ERROR_NOTIFICATION } from '../interceptors/http-context.tokens';

/** The picture each type shows for a resource without a photo; they ship with the app. */
const TYPE_IMAGES: Readonly<Record<string, string>> = {
  'Meeting Room': 'meeting-room.webp',
  Desk: 'desk.webp',
  'Company Car': 'company-car.webp',
  Projector: 'projector.webp',
  Camera: 'camera.webp',
};
const OTHER_TYPE_IMAGE = 'other.svg';

interface CachedPhoto {
  version: string;
  url: Observable<string | null>;
  objectUrl?: string;
}

/**
 * Resource photos (ADR-0027). An uploaded photo is only for those who may use the resource, so
 * it's fetched with the token like any other call and shown through an object URL. Each photo
 * is fetched once per version while the app runs; after a reload the browser asks again with
 * the ETag, and an unchanged photo costs a 304.
 */
@Injectable({ providedIn: 'root' })
export class ResourcePhotoService {
  private readonly http = inject(HttpClient);
  /** The latest version asked for, per resource. */
  private readonly photos = new Map<string, CachedPhoto>();

  /**
   * An object URL for the resource's photo at this version (its `photoUpdatedAt`), or null
   * when it can't be loaded: the type's picture shows then, and the next call tries again.
   */
  photoUrl(resourceId: string, version: string): Observable<string | null> {
    const known = this.photos.get(resourceId);
    if (known?.version === version) {
      return known.url;
    }

    const photo: CachedPhoto = { version, url: EMPTY };
    photo.url = this.http
      .get(`${environment.apiUrl}/resources/${encodeURIComponent(resourceId)}/photo`, {
        // The version keeps the browser from offering an older photo it still has.
        params: { v: version },
        responseType: 'blob',
        // A photo that fails to load isn't worth a message: the type's picture shows instead.
        context: new HttpContext().set(SKIP_ERROR_NOTIFICATION, true),
      })
      .pipe(
        map((blob) => (photo.objectUrl = URL.createObjectURL(blob))),
        catchError(() => {
          if (this.photos.get(resourceId) === photo) {
            this.photos.delete(resourceId);
          }
          return of(null);
        }),
        shareReplay(1),
      );

    // An image already on screen keeps showing after its object URL is revoked.
    if (known?.objectUrl) {
      URL.revokeObjectURL(known.objectUrl);
    }
    this.photos.set(resourceId, photo);
    return photo.url;
  }

  /** The picture a resource of this type shows without a photo of its own. */
  typeImage(typeName: string): string {
    return `images/resource-types/${TYPE_IMAGES[typeName] ?? OTHER_TYPE_IMAGE}`;
  }
}
