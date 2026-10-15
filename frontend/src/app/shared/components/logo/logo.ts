import { Component, input } from '@angular/core';

/**
 * SmartDesk's logo: a calendar with a tick on the brand's gradient, and the name beside it.
 * The name is real text, so a link around the logo is called "SmartDesk". It scales with
 * the font size around it; `light` is for dark or coloured backgrounds. In a container named
 * `logo` too narrow for all of it, only the mark shows, and the name is still read out.
 */
@Component({
  selector: 'app-logo',
  host: { '[class.light]': 'light()' },
  template: `
    <span class="mark" aria-hidden="true">
      <svg viewBox="0 0 24 24" focusable="false">
        <path d="M7.5 5.5h9a3 3 0 0 1 3 3v9a3 3 0 0 1-3 3h-9a3 3 0 0 1-3-3v-9a3 3 0 0 1 3-3z" />
        <path d="M8.5 3.5v4M15.5 3.5v4M4.5 10.5h15" />
        <path class="tick" d="M9 15.2l2.1 2.1 4-4.2" />
      </svg>
    </span>
    <span class="name">Smart<span class="desk">Desk</span></span>
  `,
  styles: `
    :host {
      position: relative;
      display: inline-flex;
      align-items: center;
      gap: 0.5em;
      font-weight: 800;
      letter-spacing: -0.03em;
      color: var(--color-text);
    }

    /* The whole logo is 7.5em wide. Narrower, as the app's top bar is on a phone, the name
       steps aside for the mark, hidden the way .visually-hidden hides. */
    @container logo (max-width: 7.6em) {
      .name {
        position: absolute;
        width: 1px;
        height: 1px;
        overflow: hidden;
        clip-path: inset(50%);
        white-space: nowrap;
      }
    }

    .mark {
      display: grid;
      place-items: center;
      width: 1.85em;
      height: 1.85em;
      border-radius: 0.55em;
      /* The same in both themes, like favicon.svg: the white calendar stays clear on it. */
      background: linear-gradient(135deg, #4f46e5 0%, #7c3aed 55%, #c026d3 100%);
      box-shadow:
        0 6px 14px -6px rgb(79 70 229 / 70%),
        inset 0 1px 0 rgb(255 255 255 / 25%);
    }

    svg {
      width: 1.2em;
      height: 1.2em;
      fill: none;
      stroke: #fff;
      stroke-width: 2;
      stroke-linecap: round;
      stroke-linejoin: round;
    }
    .tick {
      stroke-width: 2.4;
    }

    .desk {
      color: var(--color-primary);
    }
    @supports (background-clip: text) {
      .desk {
        background: var(--gradient-brand);
        background-clip: text;
        -webkit-text-fill-color: transparent;
      }
    }
    /* On paper, gradient text prints with a stray edge: the brand's violet instead. */
    @media print {
      .desk {
        background: none;
        color: #7c3aed;
        -webkit-text-fill-color: currentColor;
      }
    }

    :host(.light) {
      color: #fff;
    }
    :host(.light) .mark {
      background: rgb(255 255 255 / 16%);
      box-shadow: inset 0 0 0 1px rgb(255 255 255 / 30%);
    }
    :host(.light) .desk {
      color: #fff;
      background: none;
      -webkit-text-fill-color: currentColor;
    }
  `,
})
export class Logo {
  readonly light = input(false);
}
