import { Provider } from '@angular/core';
import { of } from 'rxjs';
import { ResourcePhotoService } from '../app/core/services/resource-photo.service';

/**
 * Resource photos without HTTP: a resource's photo is "blob:photo-of-<id>", and a type's
 * picture "type-picture:<type name>".
 */
export const RESOURCE_PHOTO_STUB: Provider = {
  provide: ResourcePhotoService,
  useValue: {
    photoUrl: (resourceId: string) => of(`blob:photo-of-${resourceId}`),
    typeImage: (typeName: string) => `type-picture:${typeName}`,
  },
};
