import { FormControl, FormGroup } from '@angular/forms';
import { ApiError } from '../../core/models/api-error.model';
import { applyServerErrors } from './server-errors';

describe('applyServerErrors', () => {
  it('puts messages on matching controls and returns the rest', () => {
    const form = new FormGroup({ name: new FormControl(''), features: new FormControl([]) });
    const error = new ApiError(400, 'Validation failed', null, {
      Name: ['Name is taken.'],
      'Features[2]': ['Duplicate feature.'],
      ResourceTypeId: ['Unknown type.'],
    });

    const unmatched = applyServerErrors(form, error);

    expect(form.controls.name.errors).toEqual({ server: 'Name is taken.' });
    expect(form.controls.name.touched).toBe(true);
    expect(form.controls.features.errors).toEqual({ server: 'Duplicate feature.' });
    expect(unmatched).toEqual(['Unknown type.']);
  });
});
