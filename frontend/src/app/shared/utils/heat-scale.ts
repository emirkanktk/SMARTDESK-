import { formatPercent } from '@angular/common';

/**
 * The steps of how much something was used (the global `.heat-level-*` classes): the report's
 * heat map of the office day (ADR-0044) and the floor plan's use of each place (ADR-0059).
 */
export type HeatLevel = 0 | 1 | 2 | 3 | 4;

/** Where a share falls on the scale: 0 for none, then by quarters. */
export function heatLevel(share: number): HeatLevel {
  return share <= 0 ? 0 : (Math.min(4, Math.ceil(share * 4)) as HeatLevel);
}

/** The steps above none as the legend says them: "1%–25%" to "76%–100%". */
export function heatSteps(locale: string): { level: HeatLevel; text: string }[] {
  return ([1, 2, 3, 4] as const).map((level) => ({
    level,
    text: `${formatPercent((level - 1) / 4 + 0.01, locale)}–${formatPercent(level / 4, locale)}`,
  }));
}
