import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

/** The API's password policy (ASP.NET Core Identity), mirrored for quick feedback. */
export const PASSWORD_RULES = [
  {
    key: 'minLength',
    test: (v: string) => v.length >= 8,
    text: $localize`:@@password.rule.length:at least 8 characters`,
  },
  {
    key: 'lowercase',
    test: (v: string) => /[a-z]/.test(v),
    text: $localize`:@@password.rule.lowercase:a lowercase letter`,
  },
  {
    key: 'uppercase',
    test: (v: string) => /[A-Z]/.test(v),
    text: $localize`:@@password.rule.uppercase:an uppercase letter`,
  },
  {
    key: 'digit',
    test: (v: string) => /\d/.test(v),
    text: $localize`:@@password.rule.digit:a digit`,
  },
  {
    key: 'symbol',
    test: (v: string) => /[^a-zA-Z0-9]/.test(v),
    text: $localize`:@@password.rule.symbol:a symbol such as ! or -`,
  },
] as const;

export const PASSWORD_HINT = $localize`:@@password.hint:At least 8 characters, with upper and lower case letters, a digit and a symbol.`;

/** "Add a digit, a symbol such as ! or -." - what a password still lacks, as one sentence. */
export function missingRulesMessage(missing: readonly string[]): string {
  return $localize`:@@password.missing:Add ${missing.join(', ')}:missing:.`;
}

/** The messages the password forms share. */
export const PASSWORD_MESSAGES = {
  choose: () => $localize`:@@password.choose:Choose a password.`,
  chooseNew: () => $localize`:@@password.chooseNew:Choose a new password.`,
  repeat: () => $localize`:@@password.repeat:Type the password again.`,
  repeatNew: () => $localize`:@@password.repeatNew:Type the new password again.`,
  mismatch: () => $localize`:@@password.mismatch:The two passwords are different.`,
};

/**
 * `{ password: ['a digit', ...] }` listing what the value still lacks; nothing when it meets
 * every rule or is empty (pair it with Validators.required). The API has the final say.
 */
export function passwordValidator(): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const value = (control.value as string | null) ?? '';
    if (!value) {
      return null;
    }
    const missing = PASSWORD_RULES.filter((rule) => !rule.test(value)).map((rule) => rule.text);
    return missing.length ? { password: missing } : null;
  };
}

/** On a form group: the `confirm` control must equal the `password` control. */
export function matchingPasswordsValidator(password: string, confirm: string): ValidatorFn {
  return (group: AbstractControl): ValidationErrors | null => {
    const first = group.get(password)?.value as string | undefined;
    const second = group.get(confirm)?.value as string | undefined;
    return second && first !== second ? { passwordsDiffer: true } : null;
  };
}
