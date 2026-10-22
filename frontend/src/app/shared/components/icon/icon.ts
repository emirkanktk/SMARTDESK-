import { Component, computed, input } from '@angular/core';

// Path data on a 24 x 24 grid, drawn as 1.8px strokes. Circles and rounded rectangles are
// written as paths so every icon is a single <path> and nothing is set as HTML.
const circle = (cx: number, cy: number, r: number) =>
  `M${cx - r} ${cy}a${r} ${r} 0 1 0 ${2 * r} 0a${r} ${r} 0 1 0 ${-2 * r} 0`;
const rect = (x: number, y: number, w: number, h: number, r: number) =>
  `M${x + r} ${y}h${w - 2 * r}a${r} ${r} 0 0 1 ${r} ${r}v${h - 2 * r}a${r} ${r} 0 0 1 ${-r} ${r}` +
  `h${2 * r - w}a${r} ${r} 0 0 1 ${-r} ${-r}v${2 * r - h}a${r} ${r} 0 0 1 ${r} ${-r}z`;
const calendar = rect(3, 4.5, 18, 16.5, 2.5) + 'M16 2.5v4M8 2.5v4M3 10h18';

const ICONS = {
  'arrow-right': 'M5 12h14M13 6l6 6-6 6',
  bell: 'M6 8a6 6 0 1 1 12 0c0 7 3 9 3 9H3s3-2 3-9M10.3 21a1.94 1.94 0 0 0 3.4 0',
  building:
    'M4 21V5a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2v16M16 9h2a2 2 0 0 1 2 2v10M2.5 21h19M8 7.5h4M8 11.5h4M8 15.5h4',
  calendar,
  'calendar-check': calendar + 'M9 15.5l2 2 4-4',
  'calendar-plus': calendar + 'M12 13v6M9 16h6',
  chart: 'M3 3v18h18M7.5 15l3.5-4 3 3 5-6.5',
  check: 'M5 12.5l4.5 4.5L19 7.5',
  clipboard:
    'M9 5H7a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-2' +
    rect(9, 3, 6, 4, 1) +
    'M9 12h6M9 16h4',
  clock: circle(12, 12, 9) + 'M12 7v5l3.2 2',
  close: 'M6 6l12 12M18 6L6 18',
  compose: 'M12 20h9M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4L16.5 3.5z',
  dashboard:
    rect(3, 3, 7, 9, 1.5) +
    rect(14, 3, 7, 5, 1.5) +
    rect(14, 12, 7, 9, 1.5) +
    rect(3, 16, 7, 5, 1.5),
  help: circle(12, 12, 9) + 'M9.1 9a3 3 0 0 1 5.8 1c0 2-3 3-3 3M12 17h.01',
  'id-card':
    rect(3, 5, 18, 14, 2.5) +
    circle(9, 11, 2.2) +
    'M5.8 16a3.4 3.4 0 0 1 6.4 0M14.5 10h3.5M14.5 13.5h3.5',
  inbox:
    'M21.5 12.5H16l-1.8 2.7h-4.4L8 12.5H2.5M5.6 5.2L2.5 12.5v5.5a2 2 0 0 0 2 2h15a2 2 0 0 0 2-2v-5.5' +
    'l-3.1-7.3A2 2 0 0 0 16.6 4H7.4a2 2 0 0 0-1.8 1.2z',
  key: circle(8, 15, 4) + 'M10.9 12.1l8.6-8.6M16 7l2.5 2.5M18.5 4.5L21 7',
  logout: 'M9 21H6a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h3M16 17l5-5-5-5M21 12H9',
  map: 'M3 6.5L9 4l6 2.5L21 4v13.5L15 20l-6-2.5L3 20V6.5zM9 4v13.5M15 6.5V20',
  'map-pin': 'M12 21s-7-6.2-7-11.5a7 7 0 0 1 14 0C19 14.8 12 21 12 21z' + circle(12, 9.5, 2.5),
  menu: 'M4 6.5h16M4 12h16M4 17.5h16',
  // A person and a key, as passkeys are drawn (ADR-0057).
  passkey:
    circle(9, 7.5, 3.5) +
    'M2.5 20.5a6.5 6.5 0 0 1 13 0' +
    circle(19, 9.5, 2.5) +
    'M19 12v9M19 16.5h2M19 19h1.5',
  search: circle(11, 11, 7) + 'M20 20l-3.9-3.9',
  send: 'M21.5 2.5L10.5 13.5M21.5 2.5l-7 19-4-8-8-4 19-7z',
  shield: 'M12 3L4.5 6v5.5c0 4.5 3.2 8.2 7.5 9.5 4.3-1.3 7.5-5 7.5-9.5V6L12 3zM9 12l2 2 4-4',
  sliders: 'M4 7h9M17 7h3M4 17h3M11 17h9' + circle(15, 7, 2) + circle(9, 17, 2),
  sparkles:
    'M12 3l1.9 5.1L19 10l-5.1 1.9L12 17l-1.9-5.1L5 10l5.1-1.9L12 3zM19 15l.8 2.2L22 18l-2.2.8L19 21l-.8-2.2L16 18l2.2-.8L19 15z',
  star: 'M12 2.8l2.5 6.1 6.5.5-5 4.2 1.6 6.4L12 16.5l-5.6 3.5 1.6-6.4-5-4.2 6.5-.5z',
  sun:
    circle(12, 12, 4) +
    'M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4',
  team:
    circle(9, 8, 3.5) +
    'M2.5 20a6.5 6.5 0 0 1 13 0M16 4.6a3.5 3.5 0 0 1 0 6.8M18.5 20a6.5 6.5 0 0 0-2.8-5.3',
  user: circle(12, 8, 4) + 'M4.5 20.5a7.5 7.5 0 0 1 15 0',
  wrench:
    'M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.8-3.8a6 6 0 0 1-7.9 7.9l-6.9 6.9a2.1 2.1 0 0 1-3-3' +
    'l6.9-6.9a6 6 0 0 1 7.9-7.9l-3.8 3.8z',
  zap: 'M13 2L4.5 13.5H12L11 22l8.5-11.5H12L13 2z',
} as const;

export type IconName = keyof typeof ICONS;

/**
 * A line icon from the app's small set, in the text's colour and 1.25em square unless a
 * style says otherwise. Always decoration: whatever it stands for is written next to it.
 * `--icon-fill` fills it in, as a star that's on.
 */
@Component({
  selector: 'app-icon',
  host: { 'aria-hidden': 'true' },
  template: `<svg viewBox="0 0 24 24" focusable="false"><path [attr.d]="path()" /></svg>`,
  styles: `
    :host {
      display: inline-flex;
      flex: none;
      width: 1.25em;
      height: 1.25em;
    }
    svg {
      width: 100%;
      height: 100%;
      fill: var(--icon-fill, none);
      stroke: currentColor;
      stroke-width: 1.8;
      stroke-linecap: round;
      stroke-linejoin: round;
    }
  `,
})
export class Icon {
  readonly name = input.required<IconName>();
  protected readonly path = computed(() => ICONS[this.name()]);
}
