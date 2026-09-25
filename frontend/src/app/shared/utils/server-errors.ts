import { AbstractControl, FormGroup } from '@angular/forms';
import { ApiError } from '../../core/models/api-error.model';

/**
 * Puts the API's validation messages (400, field -> messages) on the matching form controls
 * as a `server` error, so they render next to the field like client-side errors do. The API
 * uses PascalCase property paths ("Name", "Features[2]"); a path is matched to the control
 * named after its first segment. Returns the messages no control could take, for a general
 * alert.
 */
export function applyServerErrors(form: FormGroup, error: ApiError): string[] {
  const unmatched: string[] = [];

  for (const [path, messages] of Object.entries(error.fieldErrors)) {
    const control = controlFor(form, path);
    if (control && messages.length > 0) {
      control.setErrors({ ...control.errors, server: messages[0] });
      control.markAsTouched();
    } else {
      unmatched.push(...messages);
    }
  }

  return unmatched;
}

function controlFor(form: FormGroup, path: string): AbstractControl | null {
  const first = path.split(/[.[]/)[0];
  if (!first) {
    return null;
  }
  return form.get(first.charAt(0).toLowerCase() + first.slice(1));
}
