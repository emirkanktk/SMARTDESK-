import { DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl } from '@angular/forms';
import { fromDateTimeLocal, toDateTimeLocal } from './date-time';

/**
 * Moving the start moves the end with it, keeping the chosen duration - the way calendar
 * apps behave, and it saves re-picking the end every time. Call it once both datetime-local
 * controls hold their initial values; the duration is measured from those.
 */
export function keepDurationWhenStartMoves(
  start: FormControl<string>,
  end: FormControl<string>,
  destroyRef: DestroyRef,
): void {
  let previousStart = fromDateTimeLocal(start.value);

  start.valueChanges.pipe(takeUntilDestroyed(destroyRef)).subscribe((value) => {
    const nextStart = fromDateTimeLocal(value);
    const currentEnd = fromDateTimeLocal(end.value);
    if (nextStart && previousStart && currentEnd && currentEnd > previousStart) {
      const shift = nextStart.getTime() - previousStart.getTime();
      end.setValue(toDateTimeLocal(new Date(currentEnd.getTime() + shift)));
    }
    previousStart = nextStart;
  });
}
