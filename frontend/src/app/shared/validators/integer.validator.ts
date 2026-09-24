import { AbstractControl, ValidationErrors } from '@angular/forms';

/** Whole numbers only; empty values are left to `required`. */
export function integerValidator(control: AbstractControl): ValidationErrors | null {
  const value = control.value;
  return value === null || value === '' || Number.isInteger(Number(value))
    ? null
    : { integer: true };
}
