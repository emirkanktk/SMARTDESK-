import { FormControl, FormGroup } from '@angular/forms';
import { matchingPasswordsValidator, passwordValidator } from './password.validator';

describe('password validators', () => {
  const check = (value: string) => passwordValidator()(new FormControl(value));

  it('accepts a password that meets every rule, and leaves an empty one to required', () => {
    expect(check('Brand-New-Pass-7')).toBeNull();
    expect(check('')).toBeNull();
  });

  it('lists everything a weak password still lacks', () => {
    expect(check('short')).toEqual({
      password: [
        'at least 8 characters',
        'an uppercase letter',
        'a digit',
        'a symbol such as ! or -',
      ],
    });
    expect(check('ALLUPPERCASE1!')).toEqual({ password: ['a lowercase letter'] });
  });

  it('flags a repeat that differs, but not one still empty', () => {
    const group = (password: string, confirm: string) =>
      matchingPasswordsValidator(
        'password',
        'confirm',
      )(new FormGroup({ password: new FormControl(password), confirm: new FormControl(confirm) }));

    expect(group('Pass-word-1', 'Pass-word-2')).toEqual({ passwordsDiffer: true });
    expect(group('Pass-word-1', 'Pass-word-1')).toBeNull();
    expect(group('Pass-word-1', '')).toBeNull();
  });
});
